using Leistd.MultiTenancy.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leistd.MultiTenancy.Tests;

/// <summary>
/// 租户落值拦截器：新增实体自动填充当前租户，显式赋值不覆盖，宿主上下文保持 null。
/// </summary>
public class TenantStampingInterceptorTests : IAsyncLifetime
{
    private ServiceProvider _provider = default!;
    private SqliteConnection _connection = default!;
    private TestFilterDbContext _db = default!;
    private ICurrentTenant _currentTenant = default!;

    public async Task InitializeAsync()
    {
        _provider = FilterTestServices.Create();
        _currentTenant = _provider.GetRequiredService<ICurrentTenant>();

        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TestFilterDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new MultiTenantSaveChangesInterceptor(_currentTenant))
            .Options;

        _db = new TestFilterDbContext(options, _provider);
        await _db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
        await _provider.DisposeAsync();
    }

    [Fact]
    public async Task Added_entity_gets_current_tenant_id()
    {
        var tenantId = Guid.NewGuid();
        var order = new TestOrder { Title = "stamped" };

        using (_currentTenant.Change(tenantId))
        {
            _db.Add(order);
            await _db.SaveChangesAsync();
        }

        Assert.Equal(tenantId, order.TenantId);
    }

    [Fact]
    public async Task Explicitly_assigned_tenant_id_is_not_overwritten()
    {
        var explicitTenant = Guid.NewGuid();
        var order = new TestOrder { Title = "explicit", TenantId = explicitTenant };

        using (_currentTenant.Change(Guid.NewGuid()))
        {
            _db.Add(order);
            await _db.SaveChangesAsync();
        }

        Assert.Equal(explicitTenant, order.TenantId);
    }

    [Fact]
    public async Task Host_context_leaves_tenant_id_null()
    {
        var order = new TestOrder { Title = "host" };

        _db.Add(order);
        await _db.SaveChangesAsync();

        Assert.Null(order.TenantId);
    }

    [Fact]
    public async Task Modified_entity_is_not_restamped()
    {
        var tenantId = Guid.NewGuid();
        var order = new TestOrder { Title = "created" };

        using (_currentTenant.Change(tenantId))
        {
            _db.Add(order);
            await _db.SaveChangesAsync();
        }

        // 在另一个租户上下文里修改：TenantId 不被改写（落值只发生在 Added）
        using (_currentTenant.Change(Guid.NewGuid()))
        using (_provider.GetRequiredService<Leistd.Ddd.Domain.DataFilters.IDataFilter>().Disable<IMultiTenant>())
        {
            order.Title = "updated";
            await _db.SaveChangesAsync();
        }

        Assert.Equal(tenantId, order.TenantId);
    }
}
