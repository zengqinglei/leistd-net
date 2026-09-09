using Leistd.Settings.Abstractions;
using Leistd.Settings.Definitions;
using Leistd.Settings.Exceptions;

namespace Leistd.Settings.Services;

/// <summary>
/// 写入前对照定义校验设置名与层级。
/// </summary>
/// <param name="definitionManager">设置定义。</param>
/// <param name="store">设置值存储。</param>
public sealed class DefaultSettingManager(
    ISettingDefinitionManager definitionManager,
    ISettingStore store) : ISettingManager
{
    /// <inheritdoc />
    public Task SetAsync(
        string name,
        string? value,
        SettingScopes scope,
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        var definition = definitionManager.GetOrNull(name) ?? throw new UndefinedSettingException(name);

        // 只有这三个是"能落到某一行"的层级：None 不是层级，All 是定义侧的"两层都允许"。
        if (scope is not (SettingScopes.Tenant or SettingScopes.User or SettingScopes.Host)
            || !definition.Scopes.HasFlag(scope))
        {
            throw new SettingScopeNotAllowedException(name, scope, definition.Scopes);
        }

        if (scope == SettingScopes.User)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        }

        return store.SetAsync(name, value, scope, scope == SettingScopes.User ? userId : null, cancellationToken);
    }
}
