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
    /// 没有当前工作单元时始终创建新边界。并入当前边界时，
    /// <paramref name="options"/> 不会改变已运行工作单元的选项。
    /// </remarks>
    /// <example>
    /// <code>
    /// using var uow = await unitOfWorkManager.BeginAsync();
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
    Task<IUnitOfWork> BeginAsync(UnitOfWorkOptions? options = null, bool requiresNew = false);
}
