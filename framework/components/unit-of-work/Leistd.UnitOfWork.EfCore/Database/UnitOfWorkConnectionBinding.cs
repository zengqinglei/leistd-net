namespace Leistd.UnitOfWork.EfCore.Database;

/// <summary>固定一个工作单元的租户与物理连接目标。</summary>
internal sealed class UnitOfWorkConnectionBinding
{
    private bool _isBound;
    private Guid? _tenantId;
    private string? _targetKey;

    public string? TargetKey => _targetKey;

    public void EnsureTenant(Guid? tenantId)
    {
        if (_isBound && _tenantId != tenantId)
        {
            throw new InvalidOperationException("The current tenant cannot change inside an active unit of work.");
        }
    }

    public void Bind(Guid? tenantId, string targetKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetKey);

        if (!_isBound)
        {
            _tenantId = tenantId;
            _targetKey = targetKey;
            _isBound = true;
            return;
        }

        if (_tenantId != tenantId)
        {
            throw new InvalidOperationException("The current tenant cannot change inside an active unit of work.");
        }

        if (!string.Equals(_targetKey, targetKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The physical database target cannot change inside an active unit of work.");
        }
    }
}
