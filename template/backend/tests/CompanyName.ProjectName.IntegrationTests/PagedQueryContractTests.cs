#if (LocalIdentity)
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Leistd.Ddd.Application.Contracts.Dtos;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>
/// 列表查询的分页与排序契约：越界与非法排序都必须是 400，且排序字段限于白名单。
/// </summary>
/// <remarks>
/// <para>钉两件可观察的事：越界分页与非法排序都返回 400（而不是 500），
/// 以及可排序字段只限于各接口自己的白名单——按实体上真实存在、但不属于该列表契约的字段
/// （<c>PasswordHash</c>、<c>IsSuperAdmin</c>）排序必须被拒绝。</para>
/// </remarks>
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

    /// <summary>上限存在且生效：越过 <see cref="PagedRequestDto.MaximumLimit"/> 即 400</summary>
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
                $"/api/v1/users?offset=0&limit={PagedRequestDto.MaximumLimit}")).StatusCode);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await superAdmin.Client.GetAsync(
                $"/api/v1/users?offset=0&limit={PagedRequestDto.MaximumLimit + 1}")).StatusCode);
    }

    /// <summary>只有白名单内的字段可排序，其余一律 400</summary>
    [Theory]
    [InlineData("/api/v1/users", "username desc", true)]
    [InlineData("/api/v1/users", "email asc", true)]
    [InlineData("/api/v1/users", "creationTime desc", true)]
    // 实体上真实存在、但不属于本列表契约的字段
    [InlineData("/api/v1/users", "passwordHash asc", false)]
    [InlineData("/api/v1/users", "isSuperAdmin desc", false)]
    [InlineData("/api/v1/users", "nonsense asc", false)]
    // 方向词只认 asc/desc
    [InlineData("/api/v1/users", "username sideways", false)]
    [InlineData("/api/v1/roles", "displayName desc", true)]
    [InlineData("/api/v1/roles", "sort asc", true)]
    [InlineData("/api/v1/roles", "name asc", false)]
    public async Task Sorting_is_limited_to_the_documented_fields(
        string path, string sorting, bool expectedAccepted)
    {
        using var superAdmin = await Factory.LoginAsync("admin", ProjectWebApplicationFactory.TestAdminPassword);

        var response = await superAdmin.Client.GetAsync(
            $"{path}?offset=0&limit=10&sorting={Uri.EscapeDataString(sorting)}");

        Assert.Equal(
            expectedAccepted ? HttpStatusCode.OK : HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    /// <summary>白名单命中的排序确实生效（不是被默默忽略）</summary>
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

    /// <remarks>
    /// 走 <see cref="JsonDocument"/> 而不是反序列化成 <c>PagedResultDto&lt;T&gt;</c>：
    /// 那个记录有两个构造函数，System.Text.Json 认不出该用哪个（与仓库里其余读列表的用例同解）。
    /// </remarks>
    /// <summary>省略 sorting 与显式传默认排序，结果必须完全一致</summary>
    /// <remarks>
    /// <para>前端即使 URL 上没有排序参数，也会把默认排序状态转成 <c>sort asc</c> 发出来。
    /// 因此"省略"和"显式默认值"是同一个列表的两种调用方式，顺序必须一样——两条路径各写一遍时
    /// 它们会各自漂移。</para>
    /// <para>用两个<b>排序号相同</b>的角色才测得出来：排序号不并列时次级键根本不参与比较。</para>
    /// </remarks>
    [Fact]
    public async Task Omitted_sorting_matches_the_explicit_default()
    {
        using var superAdmin = await Factory.LoginAsync("admin", ProjectWebApplicationFactory.TestAdminPassword);

        var suffix = Guid.CreateVersion7().ToString("N")[..8];
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
