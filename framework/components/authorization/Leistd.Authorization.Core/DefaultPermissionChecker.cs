namespace Leistd.Authorization;

/// <summary>
/// 默认权限检查器。
/// </summary>
/// <remarks>
/// 以 Scoped 注册：主体解析与授予读取在同一作用域（通常是一次 HTTP 请求）内只发生一次，
/// 之后同一作用域中的任意多次检查都是内存字典查找，不再回访数据库。
/// 判定顺序为：权限未定义或未启用一律拒绝；主体不可识别一律拒绝；超级管理员旁路；
/// 否则在用户与各角色授予的并集里查找，命中即允许，未命中即拒绝。
/// 授予是纯加法，不存在"拒绝"这一态——要收回能力应调整角色构成，而不是在权限位上做减法。
/// </remarks>
public class DefaultPermissionChecker(
    IPermissionSubjectProvider subjectProvider,
    IPermissionDefinitionManager permissionDefinitionManager,
    IPermissionGrantStore permissionGrantStore) : IPermissionChecker
{
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private PermissionSubject? _subject;
    private IReadOnlySet<string>? _granted;
    private bool _loaded;

    public async Task<bool> IsGrantedAsync(string name, CancellationToken cancellationToken = default)
    {
        if (!permissionDefinitionManager.IsEffectivelyEnabled(name))
            return false;

        await EnsureLoadedAsync(cancellationToken);

        if (_subject == null)
            return false;

        return _subject.IsSuperAdmin || IsGrantedCore(name);
    }

    public async Task<MultiplePermissionGrantResult> IsGrantedAsync(
        string[] names,
        CancellationToken cancellationToken = default)
    {
        if (names == null || names.Length == 0)
            return new MultiplePermissionGrantResult(new Dictionary<string, bool>());

        var results = names
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(x => x, _ => false, StringComparer.Ordinal);

        if (results.Count == 0)
            return new MultiplePermissionGrantResult(results);

        await EnsureLoadedAsync(cancellationToken);

        if (_subject == null)
            return new MultiplePermissionGrantResult(results);

        foreach (var name in results.Keys.ToArray())
        {
            if (!permissionDefinitionManager.IsEffectivelyEnabled(name))
                continue;

            results[name] = _subject.IsSuperAdmin || IsGrantedCore(name);
        }

        return new MultiplePermissionGrantResult(results);
    }

    private bool IsGrantedCore(string name) => _granted?.Contains(name) == true;

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_loaded)
            return;

        await _loadLock.WaitAsync(cancellationToken);
        try
        {
            if (_loaded)
                return;

            _subject = await subjectProvider.GetCurrentSubjectAsync(cancellationToken);

            // 超级管理员旁路功能权限，无需读取授予记录。
            if (_subject is { IsSuperAdmin: false })
            {
                var grants = await permissionGrantStore.GetGrantsForSubjectAsync(
                    _subject.UserId,
                    _subject.RoleIds,
                    cancellationToken);

                _granted = grants.GetGrantedNames();
            }

            _loaded = true;
        }
        finally
        {
            _loadLock.Release();
        }
    }
}
