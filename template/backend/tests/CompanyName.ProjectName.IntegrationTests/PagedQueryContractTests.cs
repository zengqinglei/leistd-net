#if (LocalIdentity)
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Leistd.Data.Paging;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>列表的分页、Dynamic LINQ 排序及凭据限制。</summary>
public sealed class PagedQueryContractTests(ProjectWebApplicationFactory factory)
    : AuthorizationTestBase(factory), IClassFixture<ProjectWebApplicationFactory>
{
    [Theory]
    [InlineData("offset=-1&limit=10")]
    [InlineData("offset=0&limit=0")]
    public async Task Out_of_range_paging_is_rejected_with_400(string query)
    {
        using var superAdmin = await Factory.LoginAsync("admin", ProjectWebApplicationFactory.TestAdminPassword);

        var response = await superAdmin.Client.GetAsync($"/api/v1/users?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>上限存在且生效：越过 <see cref="PageRequest.MaximumLimit"/> 即 400</summary>
    /// <remarks>
    /// 上限本身不是业务分页大小，只是把"一个拥有列表权限的调用方单次能要走多少"封顶。
    /// 因此用例断言的是边界两侧，而不是某个具体数字。
    /// </remarks>
    [Fact]
    public async Task Limit_is_capped()
    {
        using var superAdmin = await Factory.LoginAsync("admin", ProjectWebApplicationFactory.TestAdminPassword);

        Assert.Equal(
            HttpStatusCode.OK,
            (await superAdmin.Client.GetAsync(
                $"/api/v1/users?offset=0&limit={PageRequest.MaximumLimit}")).StatusCode);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await superAdmin.Client.GetAsync(
                $"/api/v1/users?offset=0&limit={PageRequest.MaximumLimit + 1}")).StatusCode);
    }

    /// <summary>可查询属性直接排序；凭据由业务规则拒绝，解析异常交给框架。</summary>
    [Theory]
    [InlineData("/api/v1/users", "username desc", HttpStatusCode.OK)]
    [InlineData("/api/v1/users", "email asc", HttpStatusCode.OK)]
    [InlineData("/api/v1/users", "creationTime desc", HttpStatusCode.OK)]
    [InlineData("/api/v1/users", "passwordHash asc", HttpStatusCode.BadRequest)]
    [InlineData("/api/v1/users", "isSuperAdmin desc", HttpStatusCode.OK)]
    [InlineData("/api/v1/users", "DisplayName asc, Username desc", HttpStatusCode.OK)]
    [InlineData("/api/v1/users", "lastLogin.Time desc, USERNAME ASC", HttpStatusCode.OK)]
    [InlineData("/api/v1/users", "securityStamp", HttpStatusCode.BadRequest)]
    [InlineData("/api/v1/users", "username, TwoFactor.Secret", HttpStatusCode.BadRequest)]
    [InlineData("/api/v1/users", "twofactor.recoverycodes", HttpStatusCode.BadRequest)]
    [InlineData("/api/v1/users", "PasswordHash.Substring(0,1)", HttpStatusCode.BadRequest)]
    [InlineData("/api/v1/users", "iif(PasswordHash > \"$2b\", 0, 1)", HttpStatusCode.InternalServerError)]
    [InlineData("/api/v1/users", "Username,", HttpStatusCode.InternalServerError)]
    [InlineData("/api/v1/users", "nonsense asc", HttpStatusCode.InternalServerError)]
    [InlineData("/api/v1/users", "username sideways", HttpStatusCode.InternalServerError)]
    [InlineData("/api/v1/roles", "displayName desc", HttpStatusCode.OK)]
    [InlineData("/api/v1/roles", "sort asc", HttpStatusCode.OK)]
    [InlineData("/api/v1/roles", "name asc", HttpStatusCode.OK)]
    [InlineData("/api/v1/roles", "sort descending, name ascending", HttpStatusCode.OK)]
#if (OpenIddictServer)
    [InlineData("/api/v1/open-applications", "clientType desc, ClientId asc", HttpStatusCode.OK)]
    [InlineData("/api/v1/open-applications", "Application", HttpStatusCode.InternalServerError)]
    [InlineData("/api/v1/open-applications", "Application.ClientSecret", HttpStatusCode.InternalServerError)]
#endif
    public async Task Sorting_uses_query_properties_and_rejects_invalid_or_sensitive_expressions(
        string path, string sorting, HttpStatusCode expectedStatus)
    {
        using var superAdmin = await Factory.LoginAsync("admin", ProjectWebApplicationFactory.TestAdminPassword);

        var response = await superAdmin.Client.GetAsync(
            $"{path}?offset=0&limit=10&sorting={Uri.EscapeDataString(sorting)}");

        Assert.Equal(expectedStatus, response.StatusCode);
        if (expectedStatus == HttpStatusCode.InternalServerError)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(sorting, body);
            Assert.DoesNotContain("No property or field", body);
        }
        else if (expectedStatus == HttpStatusCode.BadRequest)
        {
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("User:SortingCredentialsForbidden", body.RootElement.GetProperty("code").GetString());
        }
    }

    /// <summary>排序改变返回条目的顺序。</summary>
    [Fact]
    public async Task Accepted_sorting_actually_orders_the_result()
    {
        using var superAdmin = await Factory.LoginAsync("admin", ProjectWebApplicationFactory.TestAdminPassword);

        // 至少两行才谈得上顺序
        await CreateUserAsync(superAdmin.Client);

        var ascending = await ReadUsernamesAsync(superAdmin.Client, "username asc");
        var descending = await ReadUsernamesAsync(superAdmin.Client, "username desc");

        Assert.True(ascending.Count > 1);
        Assert.Equal(ascending.OrderBy(name => name, StringComparer.Ordinal), ascending);
        Assert.Equal(ascending.AsEnumerable().Reverse(), descending);
    }

    /// <summary>省略排序与显式默认排序的并列记录次序一致。</summary>
    [Fact]
    public async Task Omitted_sorting_matches_the_explicit_default()
    {
        using var superAdmin = await Factory.LoginAsync("admin", ProjectWebApplicationFactory.TestAdminPassword);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        foreach (var name in new[] { $"zeta_{suffix}", $"alpha_{suffix}" })
        {
            var created = await superAdmin.Client.PostAsJsonAsync("/api/v1/roles", new
            {
                name,
                displayName = name,
                sort = 4242,
                isDefault = false
            });
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        }

        var omitted = await ReadRoleNamesAsync(superAdmin.Client, sorting: null);
        var explicitDefault = await ReadRoleNamesAsync(superAdmin.Client, "sort asc");

        Assert.Equal(omitted, explicitDefault);

        // 并列排序号内按名称升序（与 GetAllAsync 同口径），因此 alpha 在 zeta 之前
        var tied = omitted.Where(name => name.EndsWith(suffix, StringComparison.Ordinal)).ToList();
        Assert.Equal([$"alpha_{suffix}", $"zeta_{suffix}"], tied);
    }

    [Theory]
    [InlineData("/api/v1/users")]
    [InlineData("/api/v1/roles")]
#if (OpenIddictServer)
    [InlineData("/api/v1/open-applications")]
#endif
    public async Task Unknown_sorting_uses_the_safe_framework_default_even_when_no_rows_match(string path)
    {
        using var admin = await Factory.LoginAsync("admin", ProjectWebApplicationFactory.TestAdminPassword);
        var response = await admin.Client.GetAsync($"{path}?keyword={Guid.NewGuid():N}&sorting=unknownProperty");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(content);
        Assert.False(body.RootElement.TryGetProperty("code", out _));
        Assert.False(body.RootElement.TryGetProperty("errors", out _));
        Assert.DoesNotContain("unknownProperty", content);
    }

    [Fact]
    public async Task Missing_roles_do_not_bypass_sorting_validation()
    {
        using var admin = await Factory.LoginAsync("admin", ProjectWebApplicationFactory.TestAdminPassword);
        var response = await admin.Client.GetAsync($"/api/v1/users?roles={Guid.NewGuid():N}&sorting=passwordHash");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Query_translation_failures_are_not_reported_as_sorting_parse_errors()
    {
        using var admin = await Factory.LoginAsync("admin", ProjectWebApplicationFactory.TestAdminPassword);
        var response = await admin.Client.GetAsync("/api/v1/users?sorting=HasLocalPassword");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task Explicit_secondary_sort_takes_precedence_over_default_tie_breakers()
    {
        using var admin = await Factory.LoginAsync("admin", ProjectWebApplicationFactory.TestAdminPassword);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        foreach (var name in new[] { $"alpha_{suffix}", $"zeta_{suffix}" })
        {
            var response = await admin.Client.PostAsJsonAsync("/api/v1/roles", new
            {
                name, displayName = name, sort = 4343, isDefault = false
            });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var names = await ReadRoleNamesAsync(admin.Client, "sort asc, name desc");
        Assert.Equal([$"zeta_{suffix}", $"alpha_{suffix}"],
            names.Where(name => name.EndsWith(suffix, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Equal_primary_keys_use_unique_ids_across_page_boundaries()
    {
        using var admin = await Factory.LoginAsync("admin", ProjectWebApplicationFactory.TestAdminPassword);
        await CreateUserAsync(admin.Client);

        async Task<List<Guid>> ReadIds(int offset, int limit)
        {
            var response = await admin.Client.GetAsync(
                $"/api/v1/users?offset={offset}&limit={limit}&sorting=isActive");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return body.RootElement.GetProperty("items").EnumerateArray()
                .Select(item => item.GetProperty("id").GetGuid()).ToList();
        }

        var all = await ReadIds(0, PageRequest.MaximumLimit);
        var first = await ReadIds(0, 1);
        var second = await ReadIds(1, 1);
        Assert.True(all.Count > 1);
        Assert.Equal(all.Take(2), first.Concat(second));
        Assert.NotEqual(first[0], second[0]);
    }

    [Theory]
    [InlineData("/api/v1/users")]
#if (OpenIddictServer)
    [InlineData("/api/v1/open-applications")]
#endif
    public async Task Dto_default_sorting_matches_blank_and_explicit_default(string path)
    {
        using var admin = await Factory.LoginAsync("admin", ProjectWebApplicationFactory.TestAdminPassword);
        async Task<List<string>> ReadIds(string query)
        {
            var response = await admin.Client.GetAsync($"{path}?limit=100&{query}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return body.RootElement.GetProperty("items").EnumerateArray()
                .Select(item => item.GetProperty("id").GetString()!).ToList();
        }

        var omitted = await ReadIds("");
        Assert.Equal(omitted, await ReadIds("sorting="));
        Assert.Equal(omitted, await ReadIds("sorting=%20%20"));
        Assert.Equal(omitted, await ReadIds("sorting=CreationTime%20desc"));
    }

    private static async Task<List<string>> ReadRoleNamesAsync(HttpClient client, string? sorting)
    {
        var query = sorting is null
            ? "offset=0&limit=50"
            : $"offset=0&limit=50&sorting={Uri.EscapeDataString(sorting)}";

        var response = await client.GetAsync($"/api/v1/roles?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("name").GetString()!)
            .ToList();
    }

    private static async Task<List<string>> ReadUsernamesAsync(HttpClient client, string sorting)
    {
        var response = await client.GetAsync(
            $"/api/v1/users?offset=0&limit=50&sorting={Uri.EscapeDataString(sorting)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("username").GetString()!)
            .ToList();
    }
}
#endif
