using System.Data.Common;
using Leistd.EventBus.Abstractions;
using Leistd.EventBus.Events;
using Leistd.ExceptionHandling;
using Leistd.MultiTenancy.Abstractions;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.MultiTenancy.Dtos;
using Leistd.MultiTenancy.EntityFrameworkCore;
using Leistd.MultiTenancy.Events;
using Leistd.MultiTenancy.Exceptions;
using Leistd.MultiTenancy.Provisioning;
using Leistd.MultiTenancy.Stores;
using Leistd.UnitOfWork;
using Leistd.UnitOfWork.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leistd.MultiTenancy.Tests.EntityFrameworkCore;

/// <summary>
/// 租户管理用例：先停用登记、同一控制面工作单元登记连接，再在新租户里开通，最后启用；
/// 失败按"清开通数据 → 删连接 → 删租户"补偿，数据库原因翻成带码的 400。
/// </summary>
public sealed class TenantManagementTests : IAsyncLifetime
{
    private readonly RecordingProvisioner _provisioner = new();
    private readonly RecordingEventBus _events = new();
    private readonly RejectingGuard _guard = new();
    private ServiceProvider _provider = default!;
    private SqliteConnection _connection = default!;
    private ITenantManagementService _service = default!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        // 错误翻译是宿主的技术适配（码表随数据库而异）：组件默认不翻译，这里登记一个假的，
        // 验的是"开通失败时编排会把驱动异常交给描述器、并把它的结果抛出去"这条接缝
        services.AddSingleton<ITenantDatabaseErrorDescriber>(new FakeErrorDescriber());
        services.AddDbContext<TestDbContext>(options => options.UseSqlite(_connection));
        services.AddUnitOfWork();
        services.AddUnitOfWorkEfCore();
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddMultiTenancyEfCore<TestDbContext>();
        services.AddSingleton<ITenantProvisioner>(_provisioner);
        services.AddSingleton<ITenantActivationGuard>(_guard);
        services.AddSingleton<ILocalEventBus>(_events);

        _provider = services.BuildServiceProvider();
        await using (var scope = _provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.EnsureCreatedAsync();
        }

        _service = _provider.GetRequiredService<ITenantManagementService>();
        _provisioner.Services = _provider;
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task A_created_tenant_is_provisioned_inside_its_own_context_then_activated()
    {
        var tenant = await _service.CreateAsync(new CreateTenantInputDto { Name = "acme", DisplayName = "Acme" });

        Assert.True(tenant.IsActive);
        Assert.Equal(tenant.Id, _provisioner.ProvisionedInTenant);
        var created = Assert.IsType<TenantChangedEvent>(Assert.Single(_events.Published));
        Assert.Equal((tenant.Id, "Acme", TenantChangeKind.Created), (created.TenantId, created.DisplayName, created.Change));
    }

    // 分库在开通之前定案：开通时解析到的已经是专属库
    [Fact]
    public async Task A_connection_string_is_registered_under_the_default_name_before_provisioning()
    {
        _provisioner.OnProvision = async (services, tenant) =>
            _provisioner.ConnectionsSeenDuringProvisioning =
                await services.GetRequiredService<ITenantConnectionDirectory>().ListAsync(tenant.Id);

        var tenant = await _service.CreateAsync(Create("acme", ("default", "Host=acme;Database=acme")));

        var registered = Assert.Single(_provisioner.ConnectionsSeenDuringProvisioning!);
        Assert.Equal("default", registered.Name);
        Assert.Equal("default", Assert.Single((await Connections().GetListAsync(tenant.Id))).Name);
    }

    /// <summary>
    /// 多服务部署一次登记多条：开通钩子第一次执行时看到的就是完整集合。
    /// </summary>
    /// <remarks>
    /// 建完租户再逐条调连接管理接口会留下"租户已启用、某条连接还没登记"的中间状态，
    /// 那一刻用该连接名的服务解析到的是回落库，数据会落错地方。
    /// </remarks>
    [Fact]
    public async Task All_named_connections_are_registered_in_one_unit_of_work_before_provisioning()
    {
        _provisioner.OnProvision = async (services, tenant) =>
            _provisioner.ConnectionsSeenDuringProvisioning =
                await services.GetRequiredService<ITenantConnectionDirectory>().ListAsync(tenant.Id);

        var tenant = await _service.CreateAsync(
            Create("acme", ("default", "Host=acme;Database=acme"), ("CRM", "Host=crm;Database=acme-crm")));

        // 名字大小写不敏感：登记与解析都用归一化后的小写
        Assert.Equal(["crm", "default"], _provisioner.ConnectionsSeenDuringProvisioning!.Select(c => c.Name).Order());
        Assert.Equal(["crm", "default"], (await Connections().GetListAsync(tenant.Id)).Select(c => c.Name).Order());
    }

    /// <summary>重名在写库前拒绝：否则第二条会以"改已有登记"的语义覆盖第一条。</summary>
    [Fact]
    public async Task A_duplicated_connection_name_is_rejected_before_anything_is_written()
    {
        var error = await Assert.ThrowsAsync<BadRequestException>(
            () => _service.CreateAsync(Create("acme", ("crm", "Host=a"), ("CRM", "Host=b"))));

        Assert.Equal(MultiTenancyErrorCodes.ConnectionNameDuplicated, error.Code);
        Assert.Null(await _service.FindByNameAsync("acme"));
    }

    [Fact]
    public async Task A_failed_provisioning_is_compensated_in_order_and_described()
    {
        _provisioner.OnProvision = (_, _) => throw new FakeDbException("3D000");

        var error = await Assert.ThrowsAsync<BadRequestException>(
            () => _service.CreateAsync(
                Create("acme", ("default", "Host=acme;Database=missing"), ("crm", "Host=crm;Database=missing"))));

        Assert.Equal(MultiTenancyErrorCodes.DedicatedDatabaseMissing, error.Code);
        Assert.NotNull(_provisioner.PurgedTenant);
        Assert.Null(await _service.FindByNameAsync("acme"));
        Assert.Empty(_events.Published);

        // 名字可复用、没有指向已删租户的孤儿连接行
        _provisioner.OnProvision = null;
        var retried = await _service.CreateAsync(Create("acme"));
        Assert.Empty(await Connections().GetListAsync(retried.Id));
        await using var scope = _provider.CreateAsyncScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<TestDbContext>()
            .Set<Leistd.MultiTenancy.EntityFrameworkCore.Entities.TenantConnectionRecord>().CountAsync());
    }

    [Fact]
    public async Task A_failure_that_is_not_a_database_error_is_rethrown_unchanged()
    {
        _provisioner.OnProvision = (_, _) => throw new InvalidOperationException("seed bug");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateAsync(new CreateTenantInputDto { Name = "acme" }));

        Assert.Equal("seed bug", error.Message);
        Assert.Null(await _service.FindByNameAsync("acme"));
    }

    // 连接串填错是最常见的错误：先校验，不建租户再靠补偿擦掉
    [Fact]
    public async Task A_malformed_connection_string_is_rejected_before_anything_is_written()
    {
        var error = await Assert.ThrowsAsync<BadRequestException>(
            () => _service.CreateAsync(Create("acme", ("default", "Host=a;=b"))));

        Assert.Equal(MultiTenancyErrorCodes.ConnectionStringInvalid, error.Code);
        Assert.Null(_provisioner.ProvisionedInTenant);
        Assert.Null(await _service.FindByNameAsync("acme"));
    }

    [Fact]
    public async Task An_invalid_input_is_unprocessable()
    {
        var error = await Assert.ThrowsAsync<UnprocessableEntityException>(
            () => _service.CreateAsync(new CreateTenantInputDto { Name = new string('x', 65) }));

        Assert.Equal("name", Assert.Single(error.ValidationErrors).Field);
    }

    [Fact]
    public async Task Activation_runs_the_guards_and_deactivation_does_not()
    {
        var tenant = await _service.CreateAsync(new CreateTenantInputDto { Name = "acme" });
        await _service.SetActivationAsync(tenant.Id, new UpdateTenantActivationInputDto { IsActive = false });
        _guard.Reject = true;

        await Assert.ThrowsAsync<BadRequestException>(
            () => _service.SetActivationAsync(tenant.Id, new UpdateTenantActivationInputDto { IsActive = true }));

        Assert.False((await _service.GetAsync(tenant.Id)).IsActive);
        Assert.Equal(1, _guard.Calls);
    }

    [Fact]
    public async Task Activating_a_missing_tenant_is_not_found_rather_than_rejected_by_a_guard()
    {
        await Assert.ThrowsAsync<TenantNotFoundException>(
            () => _service.SetActivationAsync(Guid.NewGuid(), new UpdateTenantActivationInputDto { IsActive = true }));

        Assert.Equal(0, _guard.Calls);
    }

    [Fact]
    public async Task Deletion_publishes_the_name_it_had_before_deletion()
    {
        var tenant = await _service.CreateAsync(new CreateTenantInputDto { Name = "acme", DisplayName = "Acme" });
        _events.Published.Clear();

        await _service.DeleteAsync(tenant.Id);

        var deleted = Assert.IsType<TenantChangedEvent>(Assert.Single(_events.Published));
        Assert.Equal(("Acme", TenantChangeKind.Deleted), (deleted.DisplayName, deleted.Change));
        await Assert.ThrowsAsync<TenantNotFoundException>(() => _service.GetAsync(tenant.Id));
    }

    [Fact]
    public async Task Paging_and_lookup_by_name_use_the_registry()
    {
        await _service.CreateAsync(new CreateTenantInputDto { Name = "acme" });
        await _service.CreateAsync(new CreateTenantInputDto { Name = "contoso" });

        var page = await _service.GetPagedAsync(new GetTenantPagedInputDto { Keyword = "ACME", Limit = 5 });
        var lookup = await _service.FindByNameAsync("Acme");

        Assert.Equal("acme", Assert.Single(page.Items).Name);
        Assert.Equal("acme", lookup!.Name);
    }

    [Fact]
    public async Task Connection_listing_distinguishes_a_missing_tenant_from_an_unsplit_one()
    {
        var tenant = await _service.CreateAsync(new CreateTenantInputDto { Name = "acme" });

        Assert.Empty(await Connections().GetListAsync(tenant.Id));
        await Assert.ThrowsAsync<TenantNotFoundException>(() => Connections().GetListAsync(Guid.NewGuid()));
    }

    // 连接名来自 URL：不合法是 400，不以 500 出去
    [Fact]
    public async Task An_invalid_connection_name_in_a_machine_lookup_is_a_coded_bad_request()
    {
        var tenant = await _service.CreateAsync(new CreateTenantInputDto { Name = "acme" });

        var error = await Assert.ThrowsAsync<BadRequestException>(() => Connections().GetRuntimeAsync(tenant.Id, "crm_db"));

        Assert.Equal(MultiTenancyErrorCodes.ConnectionNameInvalid, error.Code);
    }

    [Fact]
    public async Task Connections_are_managed_by_name_with_versions()
    {
        var tenant = await _service.CreateAsync(new CreateTenantInputDto { Name = "acme" });
        await _service.SetActivationAsync(tenant.Id, new UpdateTenantActivationInputDto { IsActive = false });

        var set = await Connections().SetAsync(tenant.Id, "CRM", new UpsertTenantConnectionInputDto { ExpectedVersion = null, ConnectionString = "Host=crm" });
        var runtime = await Connections().GetRuntimeAsync(tenant.Id, "crm");
        await Connections().RemoveAsync(tenant.Id, "crm", set.Version);

        Assert.Equal(("crm", 1L), (set.Name, set.Version));
        Assert.Equal("Host=crm", runtime.Connection!.ConnectionString);
        Assert.Empty(await Connections().GetListAsync(tenant.Id));
    }

    private ITenantConnectionManagementService Connections() => _provider.GetRequiredService<ITenantConnectionManagementService>();

    private sealed class RecordingProvisioner : ITenantProvisioner
    {
        public Func<IServiceProvider, TenantConfiguration, Task>? OnProvision { get; set; }

        public Guid? ProvisionedInTenant { get; private set; }

        public Guid? PurgedTenant { get; private set; }

        public IReadOnlyList<TenantConnectionEntry>? ConnectionsSeenDuringProvisioning { get; set; }

        public IServiceProvider? Services { get; set; }

        public async Task ProvisionAsync(TenantProvisioningContext context, CancellationToken cancellationToken = default)
        {
            ProvisionedInTenant = CurrentTenant.Id;
            if (OnProvision is not null)
            {
                await OnProvision(Services!, context.Tenant);
            }
        }

        public Task PurgeAsync(TenantProvisioningContext context, CancellationToken cancellationToken = default)
        {
            PurgedTenant = CurrentTenant.Id;
            return Task.CompletedTask;
        }

        private static ICurrentTenant CurrentTenant => new CurrentTenantProbe();
    }

    // 直接读环境上下文里的当前租户，证明开通与清理都发生在目标租户上下文
    private sealed class CurrentTenantProbe : ICurrentTenant
    {
        private readonly ICurrentTenantAccessor _accessor = Leistd.MultiTenancy.Services.AsyncLocalCurrentTenantAccessor.Instance;

        public bool IsAvailable => Id.HasValue;
        public Guid? Id => _accessor.Current?.TenantId;
        public string? Name => _accessor.Current?.Name;
        public IDisposable Change(Guid? id, string? name = null) => throw new NotSupportedException();
    }

    private sealed class RejectingGuard : ITenantActivationGuard
    {
        public bool Reject { get; set; }

        public int Calls { get; private set; }

        public Task EnsureCanActivateAsync(TenantConfiguration tenant, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Reject
                ? throw new BadRequestException("This tenant has no users yet.").WithCode("Tenant:ActivateWithoutUsers")
                : Task.CompletedTask;
        }
    }

    private static CreateTenantInputDto Create(string name, params (string Name, string ConnectionString)[] connections)
        => new()
        {
            Name = name,
            Connections = [.. connections.Select(c => new CreateTenantConnectionInputDto
            {
                Name = c.Name,
                ConnectionString = c.ConnectionString
            })]
        };

    private sealed class FakeDbException(string sqlState) : DbException("database failure")
    {
        public override string SqlState => sqlState;
    }

    // 只认一种码，其余返回 null——与真实宿主实现同形：认不出来的错误不翻译
    private sealed class FakeErrorDescriber : ITenantDatabaseErrorDescriber
    {
        public BusinessException? Describe(Exception error)
        {
            for (var current = error; current is not null; current = current.InnerException)
            {
                if (current is DbException { SqlState: "3D000" })
                {
                    return new BadRequestException("The database does not exist.")
                        .WithCode(MultiTenancyErrorCodes.DedicatedDatabaseMissing);
                }
            }

            return null;
        }
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

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureMultiTenancy();
        }
    }
}
