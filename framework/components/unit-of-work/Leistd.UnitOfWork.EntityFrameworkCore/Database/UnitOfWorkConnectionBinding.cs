namespace Leistd.UnitOfWork.EntityFrameworkCore.Database;

// 固定一个工作单元的连接归属与物理连接目标。
// 两个维度都要固定，缺一不可：
// 物理目标防的是"一个原子边界被静默拆成跨库操作"——两个不同的库意味着两个事务，
// 提交会变成部分成功。
// 归属防的是共享库形态下的"一个工作单元写了两个归属的数据"——那种情况下两个归属
// 解析到同一个连接串，物理目标完全相同，只比对目标发现不了。归属键对本层是不透明的，
// 由 IConnectionAffinityProvider 提供（多租户实现返回当前租户 Id）。
internal sealed class UnitOfWorkConnectionBinding
{
    private bool _isBound;
    private string? _affinityKey;
    private string? _targetKey;

    public string? TargetKey => _targetKey;

    /// <summary>在尚未解析连接前先校验归属未变，让快路径（复用已创建的 DbContext）也受约束。</summary>
    public void EnsureAffinity(string? affinityKey)
    {
        if (_isBound && !string.Equals(_affinityKey, affinityKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The connection affinity cannot change inside an active unit of work.");
        }
    }

    public void Bind(string? affinityKey, string targetKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetKey);

        if (!_isBound)
        {
            _affinityKey = affinityKey;
            _targetKey = targetKey;
            _isBound = true;
            return;
        }

        EnsureAffinity(affinityKey);

        if (!string.Equals(_targetKey, targetKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The physical database target cannot change inside an active unit of work.");
        }
    }
}
