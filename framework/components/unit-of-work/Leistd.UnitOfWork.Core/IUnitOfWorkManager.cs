using Leistd.UnitOfWork.Options;

namespace Leistd.UnitOfWork;

/// <summary>
/// 创建、复用并追踪环境工作单元。
/// </summary>
public interface IUnitOfWorkManager
{
    /// <summary>
    /// 获取当前未完成的工作单元。
    /// </summary>
    IUnitOfWork? Current { get; }

    /// <summary>
    /// 开启或复用一个工作单元边界。
    /// </summary>
    /// <param name="options">本次工作单元选项；未传入时使用宿主默认选项。</param>
    /// <param name="requiresNew">
    /// 是否强制创建独立工作单元。默认并入当前工作单元；不决定事务模式。
    /// </param>
    /// <remarks>
    /// <para>没有当前工作单元时始终创建新边界。并入当前边界时，
    /// <paramref name="options"/> 不会改变已运行工作单元的选项。</para>
    /// <para><b>由使用它的那个方法自己开启。</b>当前工作单元存放在 <see cref="AsyncLocal{T}"/> 里：
    /// 在 <c>async</c> 方法内设置的值只对该方法及其调用的下游可见，方法返回后不会带回调用方。
    /// 所以不要把"开启工作单元"抽成 <c>async</c> 辅助方法再返回给调用方——调用方拿到的对象不是它的
    /// 当前工作单元，随后的写入会各自提交，不在同一个事务里，而且没有任何报错。本方法因此是同步的。</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// using var uow = unitOfWorkManager.Begin();
    /// try
    /// {
    ///     await ImportAsync(ct);
    ///     await uow.CompleteAsync(ct);
    /// }
    /// catch
    /// {
    ///     await uow.RollbackAsync();
    ///     throw;
    /// }
    /// </code>
    /// </example>
    IUnitOfWork Begin(UnitOfWorkOptions? options = null, bool requiresNew = false);
}
