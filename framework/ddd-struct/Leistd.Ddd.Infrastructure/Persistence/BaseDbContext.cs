using Leistd.Auditing;
using Leistd.Ddd.Domain.DataFilters;
using Leistd.Ddd.Infrastructure.Persistence.Extensions;
using Leistd.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
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
        ChangeTracker.Tracked += OnEntityTracked;
    }

    protected BaseDbContext(
        DbContextOptions options,
        IServiceProvider? serviceProvider) : base(options)
    {
        _serviceProvider = serviceProvider;
        ChangeTracker.Tracked += OnEntityTracked;
    }

    /// <summary>
    /// 实体进入跟踪时即落租户值
    /// </summary>
    /// <remarks>
    /// <para><b>时机必须是"进入跟踪"而不是"保存"。</b>仓储在工作单元内不立即保存
    /// （由 UoW 统一提交），因此新增与保存之间可能跨越 <c>ICurrentTenant.Change</c> 的边界：
    /// 在租户作用域内新增、作用域退出后才提交时，若在保存时刻取当前租户，就会把该租户的数据
    /// **静默落成宿主行**——该租户自己看不见（过滤器要求 TenantId 等于当前租户），
    /// 而宿主管理员看得见。没有任何报错。</para>
    /// <para>进入跟踪的时刻就是"这条数据属于谁"的语义时刻，与后续何时提交无关。</para>
    /// <para>只处理 <c>Added</c> 且 <c>TenantId</c> 仍为 null 的实体：聚合显式赋过值的不覆盖；
    /// 宿主上下文保持 null 即宿主数据；查询materialize 出来的实体（<c>FromQuery</c>）不碰。</para>
    /// </remarks>
    private void OnEntityTracked(object? sender, EntityTrackedEventArgs e)
    {
        if (e.FromQuery || e.Entry.State != EntityState.Added)
        {
            return;
        }

        if (e.Entry.Entity is not IMultiTenant { TenantId: null })
        {
            return;
        }

        if (CurrentTenantId is { } tenantId)
        {
            e.Entry.Property(nameof(IMultiTenant.TenantId)).CurrentValue = tenantId;
        }
    }

    /// <summary>
    /// 模型构建入口。<b>已封闭</b>：全局过滤器必须在派生类的实体配置之后套用，
    /// 因此派生类改写 <see cref="ConfigureModel"/> 而不是本方法
    /// </summary>
    /// <remarks>
    /// 过滤器只能作用于当时已在模型中的实体类型。若在派生类配置之前套用，
    /// 那些经 <c>ApplyConfiguration</c> 才进入模型、又没有 <c>DbSet</c> 声明的实体
    /// （如各组件的版本表）会完全逃过软删除与租户隔离——那是静默的越权缺口，
    /// 不是可以靠约定避免的疏忽。这里用调用顺序在结构上保证覆盖完整。
    /// </remarks>
    protected sealed override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 1. 派生类的实体配置（DbSet 之外的映射都在这里进入模型）
        ConfigureModel(modelBuilder);

        // 2. 软删除过滤器：禁用时返回 true（不过滤），启用时只显示未删除数据
        modelBuilder.ApplyGlobalFilters<ISoftDelete>(SoftDeleteFilterName, e =>
            !IsSoftDeleteFilterEnabled ||
            !EF.Property<bool>(e, nameof(ISoftDelete.IsDeleted)));

        // 3. 租户隔离过滤器：禁用时返回 true（全量视角）；
        // 启用时仅显示当前租户行（宿主上下文 CurrentTenantId == null 即仅宿主行）。
        // 与软删除过滤器名称不同，二者在同一实体上 AND 叠加
        modelBuilder.ApplyGlobalFilters<IMultiTenant>(MultiTenantFilterName, e =>
            !IsMultiTenantFilterEnabled ||
            EF.Property<Guid?>(e, nameof(IMultiTenant.TenantId)) == CurrentTenantId);
    }

    /// <summary>
    /// 配置本 DbContext 的实体映射。等价于原来的 <c>OnModelCreating</c>，
    /// 但由基类保证它在全局过滤器之前执行——无需（也不应）调用 <c>base</c>
    /// </summary>
    protected virtual void ConfigureModel(ModelBuilder modelBuilder)
    {
    }
}
