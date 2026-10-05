using System.Net;
using System.Net.Http.Json;
using CompanyName.ProjectName.Application.Permissions.Provider;
using CompanyName.ProjectName.Application.RealTime;
using CompanyName.ProjectName.Application.Roles.Events;
using Leistd.Authorization.Constants;
using Leistd.Authorization.Grants;
using Leistd.EventBus.Abstractions;
using Leistd.MultiTenancy.Context;
using Leistd.MultiTenancy.Extensions;
using Leistd.UnitOfWork;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>
/// 业务实时：角色列表变化在提交之后推给本作用域内有权查看的订阅者；
/// 跨租户、宿主与租户互订、无权限、未登记的资源一律拒绝订阅。
/// </summary>
public sealed class RealTimeSubscriptionTests(ProjectWebApplicationFactory factory)
    : IClassFixture<ProjectWebApplicationFactory>
{
    private static readonly TimeSpan Silence = TimeSpan.FromSeconds(1);

    [Fact]
    public async Task A_role_list_change_reaches_a_subscriber_of_the_same_scope_after_commit()
    {
        var (session, tenantId) = await CreateRoleReaderAsync(PermissionConstant.Roles.Create);
        using var _ = session;
        await using var connection = await ConnectAsync(session);
        var changed = ListenForRoleListChanges(connection);
        await connection.InvokeAsync("Subscribe", ScopeKey(tenantId));

        using var create = await session.Client.PostAsJsonAsync(
            "/api/v1/roles",
            new { Name = $"rt_{Guid.NewGuid():N}"[..20], DisplayName = "Realtime role" });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);

        await changed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await connection.StopAsync();
    }

    [Fact]
    public async Task Changes_inside_a_unit_of_work_are_pushed_only_after_it_commits()
    {
        var (session, tenantId) = await CreateRoleReaderAsync();
        using var _ = session;
        await using var connection = await ConnectAsync(session);
        var changed = ListenForRoleListChanges(connection);
        await connection.InvokeAsync("Subscribe", ScopeKey(tenantId));

        await using (var scope = factory.Services.CreateAsyncScope())
        using (scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(tenantId))
        {
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var events = scope.ServiceProvider.GetRequiredService<ILocalEventBus>();
            var key = ScopeKey(tenantId);

            using (var rolledBack = manager.Begin())
            {
                await events.PublishAsync(new RoleListChangedEvent(key));
                await rolledBack.RollbackAsync();
            }

            await Assert.ThrowsAsync<TimeoutException>(() => changed.Task.WaitAsync(Silence));

            using (var committed = manager.Begin())
            {
                await events.PublishAsync(new RoleListChangedEvent(key));
                Assert.False(changed.Task.IsCompleted);
                await committed.CompleteAsync();
            }
        }

        await changed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await connection.StopAsync();
    }

    [Fact]
    public async Task Subscriptions_outside_the_own_scope_or_to_unknown_resources_are_rejected()
    {
        var (session, tenantId) = await CreateRoleReaderAsync();
        using var _ = session;
        await using var connection = await ConnectAsync(session);

        await connection.InvokeAsync("Subscribe", ScopeKey(tenantId));
        // 另一个租户的同名资源
        await AssertForbiddenAsync(connection, $"{Guid.CreateVersion7():N}:{AppRealTimeResources.Roles}");
#if (IncludeMultiTenancy)
        // 宿主与租户互订：键的作用域段不同即拒绝
        await AssertForbiddenAsync(connection, tenantId is null ? $"{Guid.CreateVersion7():N}:{AppRealTimeResources.Roles}" : $"host:{AppRealTimeResources.Roles}");
#endif
        // 未登记的资源，以及不带作用域的裸资源名
        await AssertForbiddenAsync(connection, ScopeKey(tenantId).Replace(AppRealTimeResources.Roles, "secrets", StringComparison.Ordinal));
        await AssertForbiddenAsync(connection, AppRealTimeResources.Roles);

        await connection.StopAsync();
    }

    [Fact]
    public async Task A_subscriber_without_the_view_permission_is_rejected()
    {
        var (session, tenantId) = await CreateSessionAsync(grant: []);
        using var _ = session;
        await using var connection = await ConnectAsync(session);

        await AssertForbiddenAsync(connection, ScopeKey(tenantId));

        await connection.StopAsync();
    }
#if (IncludeMultiTenancy)

    [Fact]
    public async Task Another_tenant_does_not_receive_the_change()
    {
        var other = Guid.CreateVersion7();
        var (session, tenantId) = await CreateRoleReaderAsync();
        using var _ = session;
        await using var connection = await ConnectAsync(session);
        var changed = ListenForRoleListChanges(connection);
        await connection.InvokeAsync("Subscribe", ScopeKey(tenantId));

        await using (var scope = factory.Services.CreateAsyncScope())
        using (scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(tenantId is null ? other : null))
        {
            var current = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
            await scope.ServiceProvider.GetRequiredService<ILocalEventBus>()
                .PublishAsync(new RoleListChangedEvent(current.ScopeKey(AppRealTimeResources.Roles)));
        }

        await Assert.ThrowsAsync<TimeoutException>(() => changed.Task.WaitAsync(Silence));
        await connection.StopAsync();
    }
#endif

    private Task<(AuthenticatedSession Session, Guid? TenantId)> CreateRoleReaderAsync(params string[] extra) =>
        CreateSessionAsync([PermissionConstant.Roles.Default, .. extra]);

#if (LocalIdentity)
    // 本地身份：宿主上下文里的普通用户，按需授予权限
    private async Task<(AuthenticatedSession Session, Guid? TenantId)> CreateSessionAsync(string[] grant)
    {
        using var admin = await factory.LoginAsync("admin", ProjectWebApplicationFactory.TestAdminPassword);
        var suffix = Guid.NewGuid().ToString("N")[..10];
        const string password = "RealTimeTests!Passw0rd";
        using var create = await admin.Client.PostAsJsonAsync("/api/v1/users", new
        {
            Username = $"rt_{suffix}",
            Email = $"rt_{suffix}@example.test",
            DisplayName = "Realtime user",
            Password = password,
            IsActive = true,
            RoleIds = Array.Empty<Guid>()
        });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var userId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await GrantAsync(null, userId, grant);
        return (await factory.LoginAsync($"rt_{suffix}", password), null);
    }
#else
    // 资源服务：签发方的主体（多租户时属于一个新租户），本服务内按需授予权限
    private async Task<(AuthenticatedSession Session, Guid? TenantId)> CreateSessionAsync(string[] grant)
    {
        var subjectId = Guid.CreateVersion7();
        var tenantId = ProjectWebApplicationFactory.NewTenantId();
        await GrantAsync(tenantId, subjectId, grant);
        return (factory.CreateResourceSession(subjectId, tenantId), tenantId);
    }
#endif

    private async Task GrantAsync(Guid? tenantId, Guid userId, string[] permissions)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        using (scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(tenantId))
        {
            var manager = scope.ServiceProvider.GetRequiredService<IPermissionGrantManager>();
            foreach (var permission in permissions)
                await manager.GrantAsync(permission, PermissionGrantProviderNames.User, userId.ToString());
        }
    }

    private static string ScopeKey(Guid? tenantId) =>
        tenantId is { } tenant ? $"{tenant:N}:{AppRealTimeResources.Roles}" : $"host:{AppRealTimeResources.Roles}";

    private static TaskCompletionSource ListenForRoleListChanges(HubConnection connection)
    {
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<object>(AppRealTimeResources.RolesChanged, _ => changed.TrySetResult());
        return changed;
    }

    private static async Task AssertForbiddenAsync(HubConnection connection, string resourceKey)
    {
        var exception = await Assert.ThrowsAnyAsync<Exception>(() => connection.InvokeAsync("Subscribe", resourceKey));
        Assert.Contains("Subscription forbidden", exception.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private async Task<HubConnection> ConnectAsync(AuthenticatedSession session)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "/hubs/realtime"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                if (!string.IsNullOrEmpty(session.Cookie))
                    options.Headers.Add("Cookie", session.Cookie);
                foreach (var (name, value) in session.AuthenticationHeaders)
                    options.Headers.Add(name, value);
            })
            .Build();
        await connection.StartAsync();
        return connection;
    }
}
