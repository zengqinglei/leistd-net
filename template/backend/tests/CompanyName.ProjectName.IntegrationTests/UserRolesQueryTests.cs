#if (IncludeIdentity)
using System.Net;
using System.Net.Http.Json;
#if (IncludeRoles)
using CompanyName.ProjectName.Domain.Users.Constants;
#endif

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>
/// 用户分页接口多角色查询的输入约束：规范化（去空白/去重）、数量上限、单项长度上限。
/// </summary>
public sealed class UserRolesQueryTests(ProjectWebApplicationFactory factory)
    : IClassFixture<ProjectWebApplicationFactory>
{
    [Fact]
    public async Task Unknown_roles_should_return_an_empty_page_instead_of_matching_everyone()
    {
        using var admin = await factory.LoginAsync("admin", "Admin@123456");

        var response = await admin.Client.GetAsync(
            "/api/v1/users?offset=0&limit=10&roles=__no_such_role__");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<PagedUsers>();
        Assert.NotNull(page);
        Assert.Equal(0, page.TotalCount);
        Assert.Empty(page.Items);
    }

#if (IncludeRoles)
    [Fact]
    public async Task Roles_filter_should_normalize_whitespace_and_duplicate_values()
    {
        using var admin = await factory.LoginAsync("admin", "Admin@123456");
        var role = Uri.EscapeDataString(AdminConstant.RoleName);
        var padded = Uri.EscapeDataString($"  {AdminConstant.RoleName}  ");

        var response = await admin.Client.GetAsync(
            $"/api/v1/users?offset=0&limit=10&roles={role}&roles={padded}&roles=%20%20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<PagedUsers>();
        Assert.NotNull(page);
        Assert.True(page.TotalCount >= 1);
        Assert.All(page.Items, user => Assert.Contains(AdminConstant.RoleName, user.Roles));
    }
#endif

    [Fact]
    public async Task Roles_filter_should_reject_more_than_twenty_items()
    {
        using var admin = await factory.LoginAsync("admin", "Admin@123456");
        var query = string.Join("&", Enumerable.Range(0, 21).Select(i => $"roles=role-{i}"));

        var response = await admin.Client.GetAsync($"/api/v1/users?offset=0&limit=10&{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Roles_filter_should_reject_an_overlong_role_name()
    {
        using var admin = await factory.LoginAsync("admin", "Admin@123456");
        var overlong = new string('r', 257);

        var response = await admin.Client.GetAsync(
            $"/api/v1/users?offset=0&limit=10&roles={overlong}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed record PagedUsers(int TotalCount, List<PagedUserItem> Items);

    private sealed record PagedUserItem(string Username, List<string> Roles);
}
#endif
