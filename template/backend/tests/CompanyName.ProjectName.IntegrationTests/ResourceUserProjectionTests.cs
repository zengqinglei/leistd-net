#if (!LocalIdentity)
using CompanyName.ProjectName.Domain.Users.Entities;
using CompanyName.ProjectName.Api.Middlewares;
using CompanyName.ProjectName.Infrastructure.Persistence;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

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
        using var session = factory.CreateResourceSession(subjectId, ProjectWebApplicationFactory.NewTenantId());

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
        using var session = factory.CreateResourceSession(subjectId, ProjectWebApplicationFactory.NewTenantId());

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

    /// <summary>
    /// 当前主体的响应契约：字段集合与缺省值固定；超管标记与角色只取本服务的授权数据。
    /// </summary>
    /// <remarks>令牌里没有用户名与邮箱时为空串，显示名与租户为空时不输出（JSON 忽略 null）。</remarks>
    [Fact]
    public async Task Current_user_reports_local_super_admin_and_roles_with_a_stable_shape()
    {
        var subjectId = Guid.CreateVersion7();
        using var session = factory.CreateResourceSession(subjectId, tenantId: null);

        var before = await session.Client.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
        Assert.Equal(
            ["email", "id", "isEmailVerified", "isSuperAdmin", "roles", "username"],
            before.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(subjectId, before.GetProperty("id").GetGuid());
        Assert.Equal("", before.GetProperty("username").GetString());
        Assert.Equal("", before.GetProperty("email").GetString());
        Assert.False(before.GetProperty("isEmailVerified").GetBoolean());
        Assert.False(before.GetProperty("isSuperAdmin").GetBoolean());
        Assert.Empty(before.GetProperty("roles").EnumerateArray());

        string roleName;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MyProjectDbContext>();
            var user = await db.Set<User>().SingleAsync(user => user.Id == subjectId);
            user.MarkAsSuperAdmin();
            var role = await db.Set<Role>().OrderBy(role => role.Name).FirstAsync();
            roleName = role.Name;
            db.Add(new UserRole(subjectId, role.Id));
            await db.SaveChangesAsync();
        }

        var after = await session.Client.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
        Assert.True(after.GetProperty("isSuperAdmin").GetBoolean());
        Assert.Equal([roleName], after.GetProperty("roles").EnumerateArray().Select(role => role.GetString()));
    }

    /// <summary>同一个 sub 的并发首访：输的一方重试后投影成功，只建一行，两个请求都成功。</summary>
    /// <remarks>
    /// <para>回归点有两处。其一，<c>EnsureProjectedAsync</c> 里曾包着 <c>InsertAsync</c> 的 catch
    /// <b>永不触发</b>：工作单元内 <c>InsertAsync</c> 只登记实体，撞键要到提交时才抛，
    /// 那时已在 try 之外——结果是 500。其二，投影失败曾会拦住请求。</para>
    /// <para>竞争靠屏障确定性地制造：两个请求都读到"不存在"、都要提交新行时才一起放行，
    /// 必有一方真实撞键。只看响应码与行数证伪不了重试——投影失败也放行、胜者总会写下那一行；
    /// 判据是"没有记下投影失败"：去掉重试，输的一方会记这条警告。</para>
    /// </remarks>
    [Fact]
    public async Task Concurrent_first_requests_for_one_subject_all_succeed_and_create_one_row()
    {
        var subjectId = Guid.CreateVersion7();
        var tenantId = ProjectWebApplicationFactory.NewTenantId();
        var race = new FirstInsertRace(subjectId, participants: 2);
        var warnings = new WarningLogCapture();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.ConfigureDbContext<MyProjectDbContext>(options => options.AddInterceptors(race));
            // 不换回标准日志工厂，"没有记下警告"就恒成立、证伪不了
            warnings.Install(services);
        }));
        using var first = ProjectWebApplicationFactory.CreateResourceSession(host, subjectId, tenantId);
        using var second = ProjectWebApplicationFactory.CreateResourceSession(host, subjectId, tenantId);

        var responses = await Task.WhenAll(
            first.Client.GetAsync("/api/health/live"),
            second.Client.GetAsync("/api/health/live"));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        foreach (var response in responses) response.Dispose();
        Assert.True(race.Released, "两个请求没有同时走到提交，竞争没有发生，用例证明不了任何事");
        // 输的一方第一次提交撞键时 EF 与工作单元会各记一条错误，那是竞争本身；要看的是投影最终有没有失败
        Assert.DoesNotContain(
            warnings.Entries,
            entry => entry.Category.EndsWith(nameof(ResourceUserProvisioningMiddleware), StringComparison.Ordinal)
                && entry.Message.Contains(subjectId.ToString(), StringComparison.Ordinal));
        Assert.Equal(1, await CountUsersAsync(subjectId, host.Services));
    }

    /// <summary>投影持续失败：只重试一次，记下警告后放行请求，不留下用户行。</summary>
    /// <remarks>投影不是安全闸门：拦下请求只会让一个可预见的写入故障把该用户的每个请求都变成 500。</remarks>
    [Fact]
    public async Task A_projection_that_keeps_failing_is_retried_once_then_logged_and_the_request_proceeds()
    {
        var subjectId = Guid.CreateVersion7();
        var failing = new FailingUserInsert(subjectId);
        var warnings = new WarningLogCapture();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.ConfigureDbContext<MyProjectDbContext>(options => options.AddInterceptors(failing));
            warnings.Install(services);
        }));
        using var session = ProjectWebApplicationFactory.CreateResourceSession(host, subjectId, ProjectWebApplicationFactory.NewTenantId());

        using var response = await session.Client.GetAsync("/api/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // 两次：首次加一次重试；不重试是 1，重试循环会大于 2
        Assert.Equal(2, failing.Attempts);
        Assert.Contains(
            warnings.Entries,
            entry => entry.Category.EndsWith(nameof(ResourceUserProvisioningMiddleware), StringComparison.Ordinal)
                && entry.Message.Contains(subjectId.ToString(), StringComparison.Ordinal));
        Assert.Equal(0, await CountUsersAsync(subjectId, host.Services));
    }

    // 只拦插入这个主体的提交，其余写入照常
    private sealed class FailingUserInsert(Guid subjectId) : SaveChangesInterceptor
    {
        private int attempts;

        public int Attempts => Volatile.Read(ref attempts);

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var inserting = eventData.Context?.ChangeTracker.Entries<User>()
                .Any(entry => entry.State == EntityState.Added && entry.Entity.Id == subjectId) == true;
            if (inserting)
            {
                Interlocked.Increment(ref attempts);
                throw new InvalidOperationException("Simulated projection failure.");
            }

            return ValueTask.FromResult(result);
        }
    }

    // 两个上下文都要提交同一个新用户行时才一起放行：两边都已读到"不存在"，提交时必有一方撞键
    private sealed class FirstInsertRace(Guid subjectId, int participants) : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrived;

        public bool Released => released.Task.IsCompletedSuccessfully;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var inserting = eventData.Context?.ChangeTracker.Entries<User>()
                .Any(entry => entry.State == EntityState.Added && entry.Entity.Id == subjectId) == true;
            if (inserting && !released.Task.IsCompleted)
            {
                if (Interlocked.Increment(ref arrived) >= participants)
                    released.TrySetResult();
                await released.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }

            return result;
        }
    }

    private async Task<User?> FindUserAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MyProjectDbContext>();
        return await db.Set<User>().IgnoreQueryFilters().FirstOrDefaultAsync(user => user.Id == id);
    }

    private Task<int> CountUsersAsync(Guid id) => CountUsersAsync(id, factory.Services);

    private static async Task<int> CountUsersAsync(Guid id, IServiceProvider services)
    {
        using var scope = services.CreateScope();
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
