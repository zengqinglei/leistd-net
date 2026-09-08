using System.Data;

namespace Leistd.UnitOfWork.Options;

/// <summary>
/// <see cref="IUnitOfWorkOptions"/> 的可变实现，用于装配默认选项与单次工作单元的选项。
/// </summary>
public class UnitOfWorkOptions : IUnitOfWorkOptions
{
    /// <inheritdoc />
    public bool IsTransactional { get; set; } = true;

    /// <inheritdoc />
    public IsolationLevel? IsolationLevel { get; set; }

    /// <inheritdoc />
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// 创建一份各项均未设置的选项。
    /// </summary>
    public UnitOfWorkOptions()
    {
    }

    /// <summary>
    /// 复制一份选项。默认选项是共享实例，按次修改前必须先复制。
    /// </summary>
    /// <returns>与当前实例各项相同的新实例。</returns>
    public UnitOfWorkOptions Clone()
    {
        return new UnitOfWorkOptions
        {
            IsTransactional = IsTransactional,
            IsolationLevel = IsolationLevel,
            Timeout = Timeout
        };
    }
}
