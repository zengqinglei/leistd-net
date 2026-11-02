using Leistd.Data.Filters;
using Leistd.MultiTenancy.Context;
using Leistd.MultiTenancy.Tenancy;
using Leistd.Webhooks.EntityFrameworkCore;
using Leistd.Webhooks.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Leistd.Webhooks.Tests.TestDoubles;

/// <summary>映射 webhook 表，并给订阅、事件与投递挂上与 DDD 基座同形的租户过滤器。</summary>
/// <remarks>按当前租户过滤，<see cref="IDataFilter"/> 关闭时放开；组件不依赖基座，租户口径靠宿主上下文的过滤器承担。</remarks>
public sealed class WebhookTestDbContext(
    DbContextOptions<WebhookTestDbContext> options,
    ICurrentTenant currentTenant,
    IDataFilter dataFilter) : DbContext(options)
{
    private Guid? CurrentTenantId => currentTenant.Id;

    private bool IsMultiTenantFilterEnabled => dataFilter.IsEnabled<IMultiTenant>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ConfigureWebhooks();
        modelBuilder.Entity<WebhookSubscription>().HasQueryFilter(x => !IsMultiTenantFilterEnabled || x.TenantId == CurrentTenantId);
        modelBuilder.Entity<WebhookEvent>().HasQueryFilter(x => !IsMultiTenantFilterEnabled || x.TenantId == CurrentTenantId);
        modelBuilder.Entity<WebhookDelivery>().HasQueryFilter(x => !IsMultiTenantFilterEnabled || x.TenantId == CurrentTenantId);
    }
}
