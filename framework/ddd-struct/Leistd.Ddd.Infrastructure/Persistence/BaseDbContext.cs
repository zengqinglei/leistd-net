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
/// <para><b>新增实体的环境值（TenantId、创建审计）在"进入跟踪"时落定，本类负责</b>；
/// 修改与删除审计留在 <c>AuditSaveChangesInterceptor</c>（保存时）。
/// 分界线见 <see cref="ApplyConceptsForAddedEntity"/>。</para>
/// <para>其余保存时职责仍在拦截器：</para>
/// <para>- AuditSaveChangesInterceptor：修改/删除审计与软删除转换</para>
/// <para>- LocalEventSaveChangesInterceptor：本地事件收集和发布</para>
/// <para>本类还负责两个命名全局查询过滤器：软删除（<see cref="ISoftDelete"/>）与
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

    private IAuditPropertySetter? _auditPropertySetter;
    private bool _auditPropertySetterResolved;

    /// <summary>
    /// 创建审计设置器。未注册审计组件时为 null（新增实体只落 TenantId）
    /// </summary>
    /// <remarks>
    /// 解析一次后缓存：注册形态是 <c>Transient</c>，而本钩子按实体逐个触发，
    /// 批量插入时每行都解析一次纯属浪费。缓存实例是安全的——
    /// <c>AuditPropertySetter</c> 自身无状态，<c>ICurrentUser</c> 在每次取值时
    /// 才读 <c>ICurrentPrincipalAccessor</c>，不会把主体固化在构造时刻。
    /// </remarks>
    protected virtual IAuditPropertySetter? AuditPropertySetter
    {
        get
        {
            if (!_auditPropertySetterResolved)
            {
                _auditPropertySetter = _serviceProvider?.GetService<IAuditPropertySetter>();
                _auditPropertySetterResolved = true;
            }

            return _auditPropertySetter;
        }
    }

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
        SubscribeTrackingHooks();
    }

    protected BaseDbContext(
        DbContextOptions options,
        IServiceProvider? serviceProvider) : base(options)
    {
        _serviceProvider = serviceProvider;
        SubscribeTrackingHooks();
    }

    /// <summary>
    /// 订阅变更跟踪事件，作为"新增实体环境值"的落点
    /// </summary>
    /// <remarks>
    /// <para>两个事件都要订阅。<see cref="ChangeTracker.Tracked"/> 只在实体**首次进入**跟踪时触发，
    /// 覆盖不了"查询出来（<c>Unchanged</c>）之后才被改成 <c>Added</c>"的情形（upsert 类写法）；
    /// 那种迁移只有 <see cref="ChangeTracker.StateChanged"/> 能看到。
    /// 只订阅前者会留下一个不落值的缺口。</para>
    /// </remarks>
    private void SubscribeTrackingHooks()
    {
        ChangeTracker.Tracked += OnEntityTracked;
        ChangeTracker.StateChanged += OnEntityStateChanged;
    }

    private void OnEntityTracked(object? sender, EntityTrackedEventArgs e)
    {
        // 查询物化出来的实体一律不碰。状态判断本已足够（物化结果是 Unchanged），
        // 这行是把意图写在代码上的显式护栏
        if (e.FromQuery)
        {
            return;
        }

        ApplyConceptsForAddedEntity(e.Entry);
    }

    private void OnEntityStateChanged(object? sender, EntityStateChangedEventArgs e)
    {
        if (e.NewState != EntityState.Added)
        {
            return;
        }

        ApplyConceptsForAddedEntity(e.Entry);
    }

    /// <summary>
    /// 新增实体的环境值在此落定：租户归属与创建审计
    /// </summary>
    /// <remarks>
    /// <para><b>时机必须是"进入跟踪"而不是"保存"。</b>仓储在工作单元内不立即保存
    /// （由 UoW 统一提交），因此新增与保存之间可能跨越 <c>ICurrentTenant.Change</c> 或
    /// <c>ICurrentPrincipalAccessor.Change</c> 的边界：在作用域内新增、作用域退出后才提交时，
    /// 若在保存时刻取环境值，租户会把该租户的数据**静默落成宿主行**
    /// （该租户自己看不见，宿主管理员看得见），创建者会落成外层主体。两者都不报错。</para>
    /// <para>进入跟踪的时刻就是"这条数据属于谁、由谁创建"的语义时刻，与后续何时提交无关。
    /// 落盘时机是基础设施的调度结果，把身份绑在它上面等于让审计值随事务边界漂移。</para>
    /// <para><b>只覆盖新增，且只在值仍为空时写入</b>：显式赋过值的（种子、导入、迁移）不动。
    /// 修改与删除审计不在这里——那两种状态是迁移的结果，只有保存时刻才知道最终形态，
    /// 仍由 <c>AuditSaveChangesInterceptor</c> 处理。</para>
    /// </remarks>
    private void ApplyConceptsForAddedEntity(EntityEntry entry)
    {
        if (entry.State != EntityState.Added)
        {
            return;
        }

        SetTenantId(entry);
        AuditPropertySetter?.SetCreationProperties(entry);
    }

    private void SetTenantId(EntityEntry entry)
    {
        if (entry.Entity is not IMultiTenant { TenantId: null })
        {
            return;
        }

        // 宿主上下文（CurrentTenantId == null）保持 null 即宿主数据
        if (CurrentTenantId is { } tenantId)
        {
            entry.Property(nameof(IMultiTenant.TenantId)).CurrentValue = tenantId;
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
