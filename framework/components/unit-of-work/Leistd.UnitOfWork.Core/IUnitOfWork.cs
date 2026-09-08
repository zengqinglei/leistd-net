using Leistd.UnitOfWork.Database;
using Leistd.UnitOfWork.Events;
using Leistd.UnitOfWork.Options;
using Leistd.EventBus.Events;

namespace Leistd.UnitOfWork;

/// <summary>
/// 协调数据库保存、本地事件阶段和事务边界。
/// </summary>
public interface IUnitOfWork : IDatabaseApiContainer, ITransactionApiContainer, IDisposable
{
    /// <summary>
    /// Occurs synchronously when the unit of work is disposed without completing.
    /// </summary>
    event EventHandler<UnitOfWorkFailedEventArgs>? Failed;

    /// <summary>
    /// 工作单元释放时同步触发，无论是否已完成。
    /// </summary>
    /// <remarks>
    /// <para><b>这是生命周期契约的一部分，自定义实现必须发出它。</b>
    /// <see cref="IUnitOfWorkManager"/> 为每个显式边界建了一个独立 DI 作用域，
    /// 并靠本事件回收该作用域、把环境工作单元恢复成外层的那个。不发出它，
    /// 作用域会一直挂着（其中的 scoped 服务与 DbContext 都不释放），
    /// 而后续代码看到的"当前工作单元"仍是这个已经释放的实例。</para>
    /// <para><see cref="IDisposable.Dispose"/> 必须幂等，且本事件<b>只发一次</b>：
    /// 重复发出会让管理器重复释放同一个作用域。</para>
    /// </remarks>
    event EventHandler<UnitOfWorkEventArgs>? Disposed;

    /// <summary>
    /// 获取工作单元的唯一标识。
    /// </summary>
    Guid Id { get; }

    /// <summary>
    /// 获取本工作单元的有效选项。
    /// </summary>
    IUnitOfWorkOptions Options { get; }

    /// <summary>
    /// 获取外层工作单元；顶层边界为 <see langword="null"/>。
    /// </summary>
    IUnitOfWork? Outer { get; }

    /// <summary>
    /// 获取本工作单元的服务作用域。
    /// </summary>
    /// <remarks>
    /// <para>EF Core 提供方在<b>本工作单元的作用域</b>里解析 DbContext 与连接绑定
    /// （管理器为每个新工作单元创建独立 scope），因此自定义实现必须提供它。</para>
    /// <para>子工作单元转发父级的作用域：它没有自己的生命周期，数据库与事务都登记在父级上。</para>
    /// </remarks>
    IServiceProvider ServiceProvider { get; }

    /// <summary>
    /// 获取工作单元是否已释放。
    /// </summary>
    bool IsDisposed { get; }

    /// <summary>
    /// 获取工作单元是否已完成。
    /// </summary>
    bool IsCompleted { get; }

    /// <summary>
    /// 设置嵌套边界的外层工作单元。
    /// </summary>
    void SetOuter(IUnitOfWork? outer);

    /// <summary>
    /// 使用本次边界的选项初始化工作单元。
    /// </summary>
    void Initialize(UnitOfWorkOptions options);

    /// <summary>
    /// 把已登记数据库 API 的挂起变更推送到数据库，<b>不提交事务</b>。
    /// </summary>
    /// <remarks>
    /// <b>事务型</b>下冲刷不等于提交：事务仍开着，回滚照样撤销这些变更。<b>非事务型</b>每次冲刷各自落库、不可撤回。
    /// 仅在必须先落库才能拿到值时需要：自增主键、计算列或触发器结果、保存时换发的并发标记。
    /// 已回滚时为空操作；已完成或已释放时抛 <see cref="InvalidOperationException"/>——
    /// 那之后事务已经不在，冲刷会落到事务之外。
    /// </remarks>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存变更，发布提交前事件，提交事务，再发布提交后事件。
    /// </summary>
    /// <remarks>
    /// <b>事务型</b>：<c>SaveChanges</c> 与 BeforeCommit 处理器响应 <paramref name="cancellationToken"/>，
    /// 提交前做最后一次取消检查，此时抛 <see cref="OperationCanceledException"/> 且什么都没提交；越过该边界后不再响应取消。
    /// <b>非事务型</b>：没有提交阶段，不承诺"什么都没提交"，也不在末尾追加取消检查。
    /// </remarks>
    Task CompleteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 回滚本工作单元已开启的事务。
    /// </summary>
    Task RollbackAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 登记由基础设施在工作单元阶段中发布的本地事件。
    /// </summary>
    void AddPendingEvents(IEnumerable<ILocalEvent> events);
}
