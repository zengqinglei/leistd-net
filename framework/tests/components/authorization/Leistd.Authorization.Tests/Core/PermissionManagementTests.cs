using Leistd.Authorization.Abstractions;
using Leistd.Authorization.Constants;
using Leistd.Authorization.Dtos;
using Leistd.Authorization.Events;
using Leistd.Authorization.Exceptions;
using Leistd.Authorization.Permissions;
using Leistd.EventBus.Abstractions;
using Leistd.EventBus.Events;
using Leistd.ExceptionHandling;
using Leistd.MultiTenancy.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Leistd.Authorization.Tests.Core;

/// <summary>
/// 权限管理用例：与检查器同一侧别判据、主体经宿主目录确认、替换带版本并发布事件；首次授予只写一次。
/// </summary>
public sealed class PermissionManagementTests
{
    private const string HostOnly = "App.Tenants";
    private const string BothSides = "App.Orders";
    private const string BothSidesChild = "App.Orders.Read";
    private const string Disabled = "App.Reports";
    private const string RoleKey = "role-1";

    private readonly InMemoryGrants _grants = new();
    private readonly RecordingEventBus _events = new();

    [Fact]
    public async Task A_tenant_manager_never_sees_host_side_permissions()
    {
        var service = Build(tenantId: Guid.NewGuid());

        var definitions = await service.GetDefinitionsAsync();
        var grants = await service.GetGrantsAsync(PermissionGrantProviderNames.Role, RoleKey);

        var group = Assert.Single(definitions);
        Assert.Equal("App", group.Name);
        Assert.Equal([BothSides], group.Permissions.Select(p => p.Name));
        Assert.Equal([BothSides, BothSidesChild], grants.Grants.Select(g => g.Name));
    }

    // 整组都不可用时不下发空壳，停用的权限也不可勾选
    [Fact]
    public async Task Empty_groups_and_disabled_permissions_are_left_out()
    {
        var definitions = await Build(tenantId: Guid.NewGuid()).GetDefinitionsAsync();

        Assert.DoesNotContain(definitions, g => g.Name == "System");
        Assert.DoesNotContain(definitions.SelectMany(g => g.Permissions), p => p.Name == Disabled);
    }

    [Fact]
    public async Task Current_permissions_are_the_granted_names_available_on_this_side()
    {
        _grants.Set(PermissionGrantProviderNames.Role, RoleKey, [HostOnly, BothSides, "App.Removed"]);
        var service = Build(tenantId: Guid.NewGuid(), subject: new PermissionSubject("u1", [RoleKey], IsSuperAdmin: false));

        var current = await service.GetCurrentAsync();

        Assert.Equal([BothSides], current.Permissions);
        Assert.False(current.IsSuperAdmin);
    }

    [Fact]
    public async Task A_super_admin_gets_every_available_permission()
    {
        var current = await Build(subject: new PermissionSubject("u1", [], IsSuperAdmin: true)).GetCurrentAsync();

        Assert.Equal([HostOnly, BothSides, BothSidesChild], current.Permissions);
        Assert.True(current.IsSuperAdmin);
    }

    [Fact]
    public async Task A_caller_that_is_not_a_subject_is_unauthorized()
    {
        var error = await Assert.ThrowsAsync<UnauthorizedException>(() => Build(subject: null).GetCurrentAsync());

        Assert.Equal(PermissionErrorCodes.SubjectUnavailable, error.Code);
    }

    [Fact]
    public async Task Grants_of_an_unknown_subject_are_not_found()
    {
        var service = Build();

        var read = await Assert.ThrowsAsync<NotFoundException>(() => service.GetGrantsAsync(PermissionGrantProviderNames.Role, "ghost"));
        var write = await Assert.ThrowsAsync<NotFoundException>(() => service.ReplaceGrantsAsync(
            PermissionGrantProviderNames.Role, "ghost", new ReplacePermissionGrantsInputDto { PermissionNames = [BothSides] }));

        Assert.Equal(PermissionErrorCodes.SubjectNotFound, read.Code);
        Assert.Equal(PermissionErrorCodes.SubjectNotFound, write.Code);
        Assert.Empty(_events.Published);
    }

    [Fact]
    public async Task Replacing_grants_returns_the_new_state_and_publishes_an_event()
    {
        var output = await Build().ReplaceGrantsAsync(
            PermissionGrantProviderNames.Role,
            RoleKey,
            new ReplacePermissionGrantsInputDto { ExpectedVersion = 0, PermissionNames = [BothSides] });

        Assert.Equal(1, output.Version);
        Assert.True(Assert.Single(output.Grants, g => g.Name == BothSides).Granted);
        var replaced = Assert.IsType<PermissionGrantsReplacedEvent>(Assert.Single(_events.Published));
        Assert.Equal((PermissionGrantProviderNames.Role, RoleKey, "Administrators", 1L),
            (replaced.ProviderName, replaced.ProviderKey, replaced.SubjectDisplayName, replaced.Version));
    }

    [Fact]
    public async Task A_stale_version_is_a_coded_conflict()
    {
        _grants.Set(PermissionGrantProviderNames.Role, RoleKey, [BothSides]);

        var error = await Assert.ThrowsAsync<PermissionGrantConcurrencyException>(() => Build().ReplaceGrantsAsync(
            PermissionGrantProviderNames.Role, RoleKey, new ReplacePermissionGrantsInputDto { ExpectedVersion = 0 }));

        Assert.Equal(PermissionErrorCodes.ConcurrencyConflict, error.Code);
        Assert.Empty(_events.Published);
    }

    [Fact]
    public async Task Too_many_permissions_in_one_replacement_are_rejected()
    {
        var input = new ReplacePermissionGrantsInputDto
        {
            PermissionNames = [.. Enumerable.Range(0, ReplacePermissionGrantsInputDto.MaximumPermissionCount + 1).Select(i => $"P{i}")]
        };

        var error = await Assert.ThrowsAsync<UnprocessableEntityException>(
            () => Build().ReplaceGrantsAsync(PermissionGrantProviderNames.Role, RoleKey, input));

        Assert.Equal("permissionNames", Assert.Single(error.ValidationErrors).Field);
    }

    [Fact]
    public async Task Seeding_grants_everything_available_on_the_side_only_once()
    {
        var seeder = Services().GetRequiredService<IPermissionGrantSeeder>();

        var first = await seeder.SeedAllAsync(PermissionGrantProviderNames.Role, RoleKey, MultiTenancySides.Tenant);
        _grants.Set(PermissionGrantProviderNames.Role, RoleKey, []);
        var second = await seeder.SeedAllAsync(PermissionGrantProviderNames.Role, RoleKey, MultiTenancySides.Tenant);

        Assert.Equal(2, first);
        Assert.Null(second);
    }

    [Fact]
    public void Undefined_permissions_carry_a_code_and_the_names()
    {
        var error = new UndefinedPermissionException(["App.A", "App.B"]);

        Assert.Equal(PermissionErrorCodes.UndefinedPermission, error.Code);
        Assert.Equal("App.A, App.B", error.LocalizationData["Name"]);
    }

    private IPermissionManagementService Build(Guid? tenantId = null, PermissionSubject? subject = null)
        => Services(tenantId, subject).GetRequiredService<IPermissionManagementService>();

    private IServiceProvider Services(Guid? tenantId = null, PermissionSubject? subject = null)
        => new ServiceCollection()
            .AddSingleton<ILoggerFactory, NullLoggerFactory>()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddPermissionAuthorizationCore()
            .AddSingleton<IPermissionDefinitionProvider, Definitions>()
            .AddSingleton<IPermissionGrantStore>(_grants)
            .AddSingleton<IPermissionGrantManager>(_grants)
            .AddSingleton<IPermissionSubjectProvider>(new FixedSubject(subject))
            .AddSingleton<IPermissionSubjectDirectory, Directory>()
            .AddSingleton<ICurrentTenant>(new FixedTenant(tenantId))
            .AddSingleton<ILocalEventBus>(_events)
            .BuildServiceProvider();

    private sealed class Definitions : IPermissionDefinitionProvider
    {
        public void Define(IPermissionDefinitionContext context)
        {
            context.GetOrAddGroup("System").AddPermission(HostOnly, MultiTenancySides.Host);
            var app = context.GetOrAddGroup("App");
            app.AddPermission(BothSides, MultiTenancySides.Both).AddChild(BothSidesChild);
            app.AddPermission(Disabled, MultiTenancySides.Both).IsEnabled = false;
        }
    }

    private sealed class Directory : IPermissionSubjectDirectory
    {
        public Task<PermissionSubjectInfo?> FindAsync(string providerName, string providerKey, CancellationToken cancellationToken = default)
            => Task.FromResult(providerName == PermissionGrantProviderNames.Role && providerKey == RoleKey
                ? new PermissionSubjectInfo("Administrators")
                : null);
    }

    private sealed class FixedSubject(PermissionSubject? subject) : IPermissionSubjectProvider
    {
        public Task<PermissionSubject?> GetCurrentSubjectAsync(CancellationToken cancellationToken = default) => Task.FromResult(subject);
    }

    private sealed class FixedTenant(Guid? id) : ICurrentTenant
    {
        public bool IsAvailable => Id.HasValue;
        public Guid? Id { get; } = id;
        public string? Name => null;
        public IDisposable Change(Guid? id, string? name = null) => throw new NotSupportedException();
    }

    // 按主体保存授予与版本；替换带期望版本，与 EF 实现的并发语义一致
    private sealed class InMemoryGrants : IPermissionGrantStore, IPermissionGrantManager
    {
        private readonly Dictionary<(string, string), PermissionGrantSet> _sets = [];

        public void Set(string providerName, string providerKey, IReadOnlyList<string> names)
        {
            var version = _sets.TryGetValue((providerName, providerKey), out var current) ? current.Version + 1 : 1;
            _sets[(providerName, providerKey)] = new PermissionGrantSet(providerName, providerKey, names, version);
        }

        public Task<PermissionGrantSet> GetGrantsAsync(string providerName, string providerKey, CancellationToken cancellationToken = default)
            => Task.FromResult(_sets.GetValueOrDefault((providerName, providerKey)) ?? PermissionGrantSet.Empty(providerName, providerKey));

        public Task<IReadOnlyList<PermissionGrantSet>> GetGrantsAsync(string providerName, IReadOnlyCollection<string> providerKeys, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<PermissionGrantSet>>([.. providerKeys.Select(key => GetGrantsAsync(providerName, key).Result)]);

        public async Task<SubjectPermissionGrants> GetGrantsForSubjectAsync(string userId, IReadOnlyCollection<string> roleIds, CancellationToken cancellationToken = default)
            => new(await GetGrantsAsync(PermissionGrantProviderNames.User, userId), await GetGrantsAsync(PermissionGrantProviderNames.Role, roleIds));

        public Task<long> ReplaceGrantsAsync(string providerName, string providerKey, IReadOnlyCollection<string> permissionNames, long? expectedVersion = null, CancellationToken cancellationToken = default)
        {
            var current = _sets.GetValueOrDefault((providerName, providerKey))?.Version ?? 0;
            if (expectedVersion is { } expected && expected != current)
            {
                throw new PermissionGrantConcurrencyException(providerName, providerKey, expected, current);
            }

            Set(providerName, providerKey, [.. permissionNames]);
            return Task.FromResult(current + 1);
        }

        public Task<int> RemoveProviderAsync(string providerName, string providerKey, CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task GrantAsync(string permissionName, string providerName, string providerKey, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RevokeAsync(string permissionName, string providerName, string providerKey, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingEventBus : ILocalEventBus
    {
        public List<IEvent> Published { get; } = [];

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : IEvent
        {
            Published.Add(@event);
            return Task.CompletedTask;
        }

        public Task PublishAsync(IEvent @event, CancellationToken cancellationToken = default)
        {
            Published.Add(@event);
            return Task.CompletedTask;
        }
    }
}
