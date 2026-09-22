#if (!LocalIdentity)
using CompanyName.ProjectName.Domain.Users.Entities;
using CompanyName.ProjectName.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>
/// 资源服务形态：本地用户行由令牌投影而来，不由人手工创建。
/// </summary>
/// <remarks>
/// 这一侧的用户行主键<b>就是</b>签发方的 <c>sub</c>，而角色授予按这个主键落。
/// 曾经这里放的是一个要人手填主体标识的表单：抄错一位得到的是一条永远匹配不上任何令牌、
/// 又不报错的授权。这组用例钉住替代它的机制。
/// </remarks>
public sealed class ResourceUserProjectionTests(ProjectWebApplicationFactory factory)
    : IClassFixture<ProjectWebApplicationFactory>
{
    /// <summary>首次持令牌访问就建行，且主键等于令牌里的 sub。</summary>
    [Fact]
    public async Task A_first_authenticated_request_projects_the_subject_into_a_local_row()
    {
        var subjectId = Guid.CreateVersion7();
        using var session = factory.CreateResourceSession(subjectId, Guid.CreateVersion7());

        var response = await session.Client.GetAsync("/api/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = await FindUserAsync(subjectId);
        Assert.NotNull(user);
        // 主键就是 sub：这正是"抄错一位就永远不生效"所在的那个等式，机器来填就不可能错
        Assert.Equal(subjectId, user.Id);
    }

    /// <summary>重复访问不重复建行。</summary>
    [Fact]
    public async Task Repeated_requests_do_not_create_a_second_row()
    {
        var subjectId = Guid.CreateVersion7();
        using var session = factory.CreateResourceSession(subjectId, Guid.CreateVersion7());

        await session.Client.GetAsync("/api/health/live");
        await session.Client.GetAsync("/api/health/live");

        Assert.Equal(1, await CountUsersAsync(subjectId));
    }

    /// <summary>未认证的请求不建行：没有主体就没什么可投影的。</summary>
    [Fact]
    public async Task An_anonymous_request_projects_nothing()
    {
        var before = await CountAllUsersAsync();

        using var client = ProjectWebApplicationFactory.CreateProjectClient(factory);
        await client.GetAsync("/api/health/live");

        Assert.Equal(before, await CountAllUsersAsync());
    }

    private async Task<User?> FindUserAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MyProjectDbContext>();
        return await db.Set<User>().IgnoreQueryFilters().FirstOrDefaultAsync(user => user.Id == id);
    }

    private async Task<int> CountUsersAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MyProjectDbContext>();
        return await db.Set<User>().IgnoreQueryFilters().CountAsync(user => user.Id == id);
    }

    private async Task<int> CountAllUsersAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MyProjectDbContext>();
        return await db.Set<User>().IgnoreQueryFilters().CountAsync();
    }
}
#endif
