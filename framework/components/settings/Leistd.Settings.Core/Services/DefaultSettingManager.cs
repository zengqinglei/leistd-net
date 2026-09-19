using Leistd.Settings.Abstractions;
using Leistd.Settings.Definitions;
using Leistd.Settings.Exceptions;
using Microsoft.AspNetCore.DataProtection;

namespace Leistd.Settings.Services;

/// <summary>
/// 写入前对照定义校验设置名与层级。
/// </summary>
/// <remarks>
/// 写入后让同一作用域的 <see cref="ISettingProvider"/> 重新读取：它按作用域记忆化，
/// 不作废的话，同一请求里"先写后读"读到的是写入前的值。
/// </remarks>
/// <param name="definitionManager">设置定义。</param>
/// <param name="store">设置值存储。</param>
/// <param name="settingProvider">同一作用域的设置读取器，写入后作废它的记忆化结果。</param>
/// <param name="dataProtectionProvider">
/// 机密设置（<see cref="ISettingDefinition.IsEncrypted"/>）的加密用宿主的 Data Protection；
/// 没注册时只有写入机密设置会失败。
/// </param>
public sealed class DefaultSettingManager(
    ISettingDefinitionManager definitionManager,
    ISettingStore store,
    ISettingProvider settingProvider,
    IDataProtectionProvider? dataProtectionProvider = null) : ISettingManager
{
    // 与读取器同一用途，写入的密文它才解得开
    private readonly IDataProtector? _protector =
        dataProtectionProvider?.CreateProtector(DefaultSettingProvider.ProtectionPurpose);

    /// <inheritdoc />
    public async Task SetAsync(
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

        if (definition.IsEncrypted && value is not null)
        {
            value = (_protector ?? throw DefaultSettingProvider.MissingDataProtection(name))
                .CreateProtector(name)
                .Protect(value);
        }

        await store.SetAsync(name, value, scope, scope == SettingScopes.User ? userId : null, cancellationToken);
        (settingProvider as DefaultSettingProvider)?.Invalidate();
    }
}
