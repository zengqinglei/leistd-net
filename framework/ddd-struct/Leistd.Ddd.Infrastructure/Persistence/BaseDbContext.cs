using Leistd.Auditing;
using Leistd.Ddd.Domain.DataFilters;
using Leistd.Ddd.Infrastructure.Persistence.Extensions;
using Leistd.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Leistd.Ddd.Infrastructure.Persistence;

/// <summary>
/// 基础 DbContext
/// </summary>
/// <remarks>
/// <para>审计字段和本地事件的处理已迁移至 SaveChangesInterceptor：</para>
/// <para>- AuditSaveChangesInterceptor：处理审计字段自动填充</para>
/// <para>- LocalEventSaveChangesInterceptor：处理本地事件收集和发布</para>
/// <para>- MultiTenantSaveChangesInterceptor（多租户宿主挂载）：新增实体自动填充 TenantId</para>
/// <para>本类负责两个命名全局查询过滤器：软删除（<see cref="ISoftDelete"/>）与
/// 租户隔离（<see cref="IMultiTenant"/>）。过滤器表达式捕获本实例属性，
/// EF 将其参数化并在每次查询时重估——<c>IDataFilter</c> 开关与 <c>ICurrentTenant.Change</c>
/// 即时生效，无需重建模型。</para>
/// </remarks>
public abstract class BaseDbContext : DbContext
{
    /// <summary>软删除过滤器名称</summary>
    public const string SoftDeleteFilterName = "SoftDelete";

    /// <summary>租户隔离过滤器名称</summary>
    public const string MultiTenantFilterName = "MultiTenant";

    private readonly IServiceProvider? _serviceProvider;

    /// <summary>
    /// 软删除过滤器是否启用
    /// </summary>
    protected virtual bool IsSoftDeleteFilterEnabled =>
        _serviceProvider?.GetService<IDataFilter>()?.IsEnabled<ISoftDelete>() ?? true;

    /// <summary>
    /// 租户过滤器是否启用
    /// </summary>
    protected virtual bool IsMultiTenantFilterEnabled =>
        _serviceProvider?.GetService<IDataFilter>()?.IsEnabled<IMultiTenant>() ?? true;

    /// <summary>
    /// 当前租户 Id（null = 宿主视角，仅显示宿主行）
    /// </summary>
    /// <remarks>
    /// 设计时（<c>_serviceProvider == null</c>）或未注册 <c>ICurrentTenant</c> 时为 null——
    /// 此时租户过滤器表现为宿主视角；非多租户宿主的实体不实现 <see cref="IMultiTenant"/>，
    /// 过滤器不会作用于任何实体，零运行时成本。
    /// </remarks>
    protected virtual Guid? CurrentTenantId =>
        _serviceProvider?.GetService<ICurrentTenant>()?.Id;

    protected BaseDbContext(DbContextOptions options) : base(options)
    {
    }

    protected BaseDbContext(
        DbContextOptions options,
        IServiceProvider? serviceProvider) : base(options)
    {
        _serviceProvider = serviceProvider;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 软删除过滤器：禁用时返回 true（不过滤），启用时只显示未删除数据
        modelBuilder.ApplyGlobalFilters<ISoftDelete>(SoftDeleteFilterName, e =>
            !IsSoftDeleteFilterEnabled ||
            !EF.Property<bool>(e, nameof(ISoftDelete.IsDeleted)));

        // 租户隔离过滤器：禁用时返回 true（全量视角）；
        // 启用时仅显示当前租户行（宿主上下文 CurrentTenantId == null 即仅宿主行）。
        // 与软删除过滤器名称不同，二者在同一实体上 AND 叠加
        modelBuilder.ApplyGlobalFilters<IMultiTenant>(MultiTenantFilterName, e =>
            !IsMultiTenantFilterEnabled ||
            EF.Property<Guid?>(e, nameof(IMultiTenant.TenantId)) == CurrentTenantId);
    }
}
