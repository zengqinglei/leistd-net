using System.Data;

namespace Leistd.UnitOfWork.Options;

/// <summary>
/// 工作单元的事务选项。
/// </summary>
public interface IUnitOfWorkOptions
{
    /// <summary>
    /// 本工作单元是否开启事务。未显式设置时取默认选项的值。
    /// </summary>
    bool IsTransactional { get; }

    /// <summary>
    /// 事务隔离级别，仅在 <see cref="IsTransactional"/> 为 <see langword="true"/> 时生效。
    /// 为 <see langword="null"/> 时取默认选项的值。
    /// </summary>
    IsolationLevel? IsolationLevel { get; }

    /// <summary>
    /// 工作单元的超时时长。为 <see langword="null"/> 时取默认选项的值。
    /// </summary>
    TimeSpan? Timeout { get; }
}
