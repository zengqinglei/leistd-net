#if (RemoteTokenAuth)
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompanyName.ProjectName.Application.OperationRecords.Provider;
using CompanyName.ProjectName.Domain.Users.Constants;
using CompanyName.ProjectName.Infrastructure.Persistence;
using CompanyName.ProjectName.Infrastructure.Users;
#if (IncludeOperationRecords)
using Leistd.OperationRecords.Models;
#endif
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CompanyName.ProjectName.IntegrationTests;

public sealed class ResourceAutomaticAdminTests(ProjectWebApplicationFactory factory)
    : IClassFixture<ProjectWebApplicationFactory>
{
    [Fact]
    public async Task First_host_administrator_creates_the_role_with_permissions_and_a_successful_operation_record()
    {
        using var isolatedFactory = new ProjectWebApplicationFactory();
        var subject = Guid.CreateVersion7();
        using var host = WithLookup(isolatedFactory, (tenant, id, _) =>
        {
            Assert.Equal("host", tenant);
            Assert.Equal(subject, id);
            return Task.FromResult(StubIdentityAdministratorsApi.Status(true));
        });
        using var session = ProjectWebApplicationFactory.CreateResourceSession(host, subject, null);
        var target = $"User/{subject}";
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MyProjectDbContext>();
        await db.Roles.Where(role => role.Name == AdminConstant.RoleName).ExecuteDeleteAsync();

        await AssertAdminAsync(session.Client);
        using var users = await session.Client.GetAsync("/api/v1/users?offset=0&limit=10");
        Assert.Equal(HttpStatusCode.OK, users.StatusCode);

#if (IncludeOperationRecords)
        var record = Assert.Single(await db.OperationRecords.IgnoreQueryFilters()
            .Where(record => record.Action == OperationRecordActions.ResourceAdminGranted && record.TargetId == target)
            .ToListAsync());
        Assert.Equal(OperationRecordOutcome.Succeeded, record.Outcome);
        Assert.Equal(OperationRecordAuthorizations.IdentityAdministrator, record.AuthorizationBasis);
        Assert.Equal(subject.ToString(), record.ActorId);
#else
        var record = Assert.Single(host.Services.GetRequiredService<OperationRecordLogCapture>().Snapshot(), entry =>
            OperationRecordLogCapture.Field(entry, "OperationAction") == OperationRecordActions.ResourceAdminGranted
            && OperationRecordLogCapture.Field(entry, "OperationTargetId") == target);
        Assert.Equal("Succeeded", OperationRecordLogCapture.Field(record, "OperationOutcome"));
        Assert.Equal(OperationRecordAuthorizations.IdentityAdministrator, OperationRecordLogCapture.Field(record, "OperationAuthorizationBasis"));
        Assert.Equal(subject.ToString(), OperationRecordLogCapture.Field(record, "OperationActorId"));
#endif
    }
#if (IncludeMultiTenancy)

    [Fact]
    public async Task First_tenant_administrator_receives_the_admin_role_in_the_selected_tenant()
    {
        var tenantId = Guid.CreateVersion7();
        var subject = Guid.CreateVersion7();
        using var host = WithLookup(factory, (tenant, id, _) =>
        {
            Assert.Equal(tenantId.ToString(), tenant);
            Assert.Equal(subject, id);
            return Task.FromResult(StubIdentityAdministratorsApi.Status(true));
        });
        using var session = ProjectWebApplicationFactory.CreateResourceSession(host, subject, tenantId);
        var me = await AssertAdminAsync(session.Client);

        Assert.Equal(tenantId, me.GetProperty("tenantId").GetGuid());
    }
#endif

    [Fact]
    public async Task An_ordinary_user_gets_a_local_member_with_no_roles()
    {
        var subject = Guid.CreateVersion7();
        using var host = WithLookup(factory, (tenant, id, _) =>
        {
            Assert.Equal("host", tenant);
            Assert.Equal(subject, id);
            return Task.FromResult(StubIdentityAdministratorsApi.Status(false));
        });
        using var session = ProjectWebApplicationFactory.CreateResourceSession(host, subject, null);

        var me = await session.Client.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
        Assert.Equal(subject, me.GetProperty("id").GetGuid());
        Assert.Empty(me.GetProperty("roles").EnumerateArray());
    }

    [Theory]
    [InlineData("removed", HttpStatusCode.OK, false)]
    [InlineData("disabled", HttpStatusCode.Forbidden, true)]
    [InlineData("deleted", HttpStatusCode.Forbidden, true)]
    [InlineData("administrator", HttpStatusCode.OK, true)]
    public async Task Existing_members_keep_local_authorization_without_another_identity_lookup(
        string state, HttpStatusCode expected, bool hasMembership)
    {
        var subject = Guid.CreateVersion7();
        var calls = 0;
        using var host = WithLookup(factory, (_, _, _) =>
        {
            Assert.Equal(1, Interlocked.Increment(ref calls));
            return Task.FromResult(StubIdentityAdministratorsApi.Status(true));
        });
        using var session = ProjectWebApplicationFactory.CreateResourceSession(host, subject, null);
        await AssertAdminAsync(session.Client);

        await using (var setupScope = host.Services.CreateAsyncScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<MyProjectDbContext>();
            var user = await db.Users.Include(user => user.Roles).SingleAsync(user => user.Id == subject);
            var role = Assert.Single(user.Roles);
            if (state == "removed") user.RemoveRole(role.RoleId);
            if (state == "disabled") user.Disable();
            await db.SaveChangesAsync();
            if (state == "deleted")
            {
                // 保留历史成员关系，构造一条对常规查询不可见的已有用户。
                await db.Users.Where(row => row.Id == subject)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsDeleted, true));
            }
        }

        using var response = await session.Client.GetAsync("/api/v1/auth/me");
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(1, calls);
        await using var scope = host.Services.CreateAsyncScope();
        var current = scope.ServiceProvider.GetRequiredService<MyProjectDbContext>();
        var member = await current.Users.IgnoreQueryFilters().SingleAsync(user => user.Id == subject);
        Assert.Equal(state != "disabled", member.IsActive);
        Assert.Equal(state == "deleted", member.IsDeleted);
        var membership = Assert.Single(await current.UserRoles.IgnoreQueryFilters().Where(role => role.UserId == subject).ToListAsync());
        Assert.Equal(hasMembership, !membership.IsDeleted);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_transient_identity_failure_leaves_no_member_and_the_next_request_can_bootstrap(bool timeout)
    {
        var subject = Guid.CreateVersion7();
        var calls = 0;
        using var host = WithLookup(factory, (_, _, _) => Interlocked.Increment(ref calls) switch
        {
            1 when timeout => Task.FromException<HttpResponseMessage>(new TimeoutException("Identity did not respond.")),
            1 => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)),
            _ => Task.FromResult(StubIdentityAdministratorsApi.Status(true))
        });
        using var session = ProjectWebApplicationFactory.CreateResourceSession(host, subject, null);

        using var failure = await session.Client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Forbidden, failure.StatusCode);
        Assert.Equal("User:LocalMemberMissing", (await failure.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(1, calls);
        await using (var scope = host.Services.CreateAsyncScope())
            Assert.False(await scope.ServiceProvider.GetRequiredService<MyProjectDbContext>()
                .Users.IgnoreQueryFilters().AnyAsync(user => user.Id == subject));

        await AssertAdminAsync(session.Client);
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task A_refused_identity_lookup_logs_an_error_and_creates_a_member_without_roles(HttpStatusCode status)
    {
        var subject = Guid.CreateVersion7();
        using var host = WithLookup(factory, (_, _, _) => Task.FromResult(new HttpResponseMessage(status)));
        using var session = ProjectWebApplicationFactory.CreateResourceSession(host, subject, null);

        var me = await session.Client.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
        Assert.Equal(subject, me.GetProperty("id").GetGuid());
        Assert.Empty(me.GetProperty("roles").EnumerateArray());
        Assert.Contains(host.Services.GetFakeLogCollector().GetSnapshot(), entry =>
            entry.Level == LogLevel.Error && entry.Message.Contains("Identity API scope grant", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Concurrent_first_requests_create_one_member_and_one_admin_membership()
    {
        var subject = Guid.CreateVersion7();
        var bothLookups = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var host = WithLookup(factory, async (_, _, token) =>
        {
            if (Interlocked.Increment(ref calls) == 2) bothLookups.SetResult();
            await bothLookups.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            return StubIdentityAdministratorsApi.Status(true);
        });
        using var first = ProjectWebApplicationFactory.CreateResourceSession(host, subject, null);
        using var second = ProjectWebApplicationFactory.CreateResourceSession(host, subject, null);

        await Task.WhenAll(AssertAdminAsync(first.Client), AssertAdminAsync(second.Client));

        Assert.Equal(2, calls);
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MyProjectDbContext>();
        Assert.Equal(1, await db.Users.IgnoreQueryFilters().CountAsync(user => user.Id == subject));
        var membership = Assert.Single(await db.UserRoles.IgnoreQueryFilters().Where(role => role.UserId == subject).ToListAsync());
        Assert.False(membership.IsDeleted);
    }

    private static WebApplicationFactory<Program> WithLookup(ProjectWebApplicationFactory factory,
        Func<string, Guid, CancellationToken, Task<HttpResponseMessage>> respond)
        => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<IIdentityAdministratorsApi>(new StubIdentityAdministratorsApi(respond))));

    private static async Task<JsonElement> AssertAdminAsync(HttpClient client)
    {
        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
        Assert.Equal(AdminConstant.RoleName, Assert.Single(me.GetProperty("roles").EnumerateArray()).GetString());
        return me;
    }
}
#endif
