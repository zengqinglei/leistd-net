using Leistd.MultiTenancy;
using Leistd.Authorization.Permissions;
using Leistd.Authorization.Abstractions;
using Leistd.MultiTenancy.Abstractions;

namespace Leistd.Authorization.Services;

/// <summary>
/// 默认权限检查器。
/// </summary>
/// <remarks>
/// 以 Scoped 注册：主体解析与授予读取每个作用域各发生一次，其后的检查都是内存查找。
/// 完整判定顺序见 authorization 组件文档。
/// </remarks>
public class DefaultPermissionChecker(
    IPermissionSubjectProvider subjectProvider,
    IPermissionDefinitionManager permissionDefinitionManager,
    IPermissionGrantStore permissionGrantStore,
    ICurrentTenant? currentTenant = null) : IPermissionChecker, IDisposable
{
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private PermissionSubject? _subject;
    private IReadOnlySet<string>? _granted;
    private bool _loaded;

    private MultiTenancySides CurrentSide =>
        currentTenant?.IsAvailable == true ? MultiTenancySides.Tenant : MultiTenancySides.Host;

    private bool MatchesCurrentSide(string name) =>
        permissionDefinitionManager.GetOrNull(name)?.Side.HasFlag(CurrentSide) == true;

    /// <inheritdoc />
    public async Task<bool> IsGrantedAsync(string name, CancellationToken cancellationToken = default)
    {
        if (!permissionDefinitionManager.IsEffectivelyEnabled(name))
            return false;

        // 侧别硬边界：先于授予与超管旁路——宿主侧权限在租户上下文内对任何主体都不可用
        if (!MatchesCurrentSide(name))
            return false;

        await EnsureLoadedAsync(cancellationToken);

        if (_subject == null)
            return false;

        return _subject.IsSuperAdmin || IsGrantedCore(name);
    }

    /// <inheritdoc />
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

            // 侧别硬边界与单项检查一致：不匹配即保持 false
            if (!MatchesCurrentSide(name))
                continue;

            results[name] = _subject.IsSuperAdmin || IsGrantedCore(name);
        }

        return new MultiplePermissionGrantResult(results);
    }

    private bool IsGrantedCore(string name) => _granted?.Contains(name) == true;

    /// <summary>
        /// 释放加载闸门。
    /// </summary>
    public void Dispose() => _loadLock.Dispose();

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
