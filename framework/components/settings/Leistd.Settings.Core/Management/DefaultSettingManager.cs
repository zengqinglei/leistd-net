using System.Globalization;
using Leistd.EventBus.Abstractions;
using Leistd.ExceptionHandling;
using Leistd.Settings.Definitions;
using Leistd.Settings.Errors;
using Leistd.Settings.Management;
using Leistd.Settings.Resolution;
using Leistd.Settings.Stores;
using Leistd.Settings.Events;
using Leistd.Settings.Exceptions;
using Leistd.Settings.Validation;
using Microsoft.AspNetCore.DataProtection;

namespace Leistd.Settings.Management;

/// <summary>
/// 写入前对照定义校验设置名、层级与取值，写入后发布 <see cref="SettingChangedEvent"/>。
/// </summary>
/// <remarks>
/// <para>取值校验依次是：空串拒绝、<see cref="ISettingDefinition.ValueType"/> 与区间、
/// <see cref="ISettingDefinition.AllowedValues"/>、宿主注册的 <see cref="ISettingValueValidator"/>。
/// 清除（值为 <see langword="null"/>）只校验名称与层级。</para>
/// <para>写入后让同一作用域的 <see cref="ISettingProvider"/> 重新读取：它按作用域记忆化，
/// 不作废的话，同一请求里"先写后读"读到的是写入前的值。</para>
/// </remarks>
/// <param name="definitionManager">设置定义。</param>
/// <param name="store">设置值存储。</param>
/// <param name="settingProvider">同一作用域的设置读取器，写入后作废它的记忆化结果。</param>
/// <param name="validators">业务取值校验器。</param>
/// <param name="eventBus">发布变更事件；未注册本地事件总线时不发布。</param>
/// <param name="dataProtectionProvider">
/// 机密设置（<see cref="ISettingDefinition.IsEncrypted"/>）的加密用宿主的 Data Protection；
/// 没注册时只有写入机密设置会失败。
/// </param>
public sealed class DefaultSettingManager(
    ISettingDefinitionManager definitionManager,
    ISettingStore store,
    ISettingProvider settingProvider,
    IEnumerable<ISettingValueValidator> validators,
    ILocalEventBus? eventBus = null,
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
        else
        {
            userId = null;
        }

        if (value is not null)
        {
            EnsureWellFormed(definition, value);
            var context = new SettingValueValidationContext(definition, value, scope, userId);
            foreach (var validator in validators)
            {
                await validator.ValidateAsync(context, cancellationToken);
            }
        }

        var stored = definition.IsEncrypted && value is not null
            ? (_protector ?? throw DefaultSettingProvider.MissingDataProtection(name))
                .CreateProtector(name)
                .Protect(value)
            : value;

        await store.SetAsync(name, stored, scope, userId, cancellationToken);
        (settingProvider as DefaultSettingProvider)?.Invalidate();

        if (eventBus is not null)
        {
            await eventBus.PublishAsync(new SettingChangedEvent(name, scope, userId), cancellationToken);
        }
    }

    // 只有 null 表示清除。空串若放行，会作为真实值落库：既挡住向下一层的回落，
    // 又被消费方当成"未设置"——一个值同时是两种意思。
    private static void EnsureWellFormed(ISettingDefinition definition, string value)
    {
        if (value.Length == 0)
        {
            throw new BadRequestException(
                    $"An empty value is not accepted for '{definition.Name}'. Send null to clear the override.")
                .WithCode(SettingErrorCodes.EmptyValueRejected)
                .WithData("Name", definition.Name);
        }

        switch (definition.ValueType)
        {
            case SettingValueType.Boolean when value is not ("true" or "false"):
                throw new BadRequestException($"'{value}' is not a boolean; use 'true' or 'false'.")
                    .WithCode(SettingErrorCodes.BooleanRequired)
                    .WithData("Value", value);

            case SettingValueType.Integer:
                if (!int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
                {
                    throw new BadRequestException($"'{definition.Name}' must be an integer.")
                        .WithCode(SettingErrorCodes.IntegerRequired)
                        .WithData("Name", definition.Name);
                }

                if (number < (definition.Minimum ?? int.MinValue) || number > (definition.Maximum ?? int.MaxValue))
                {
                    var minimum = definition.Minimum ?? int.MinValue;
                    var maximum = definition.Maximum ?? int.MaxValue;
                    throw new BadRequestException($"'{definition.Name}' must be an integer between {minimum} and {maximum}.")
                        .WithCode(SettingErrorCodes.ValueOutOfRange)
                        .WithData("Name", definition.Name)
                        .WithData("Minimum", minimum)
                        .WithData("Maximum", maximum);
                }

                break;
        }

        if (definition.AllowedValues is { } allowed && !allowed.Contains(value, StringComparer.Ordinal))
        {
            var candidates = string.Join(", ", allowed);
            throw new BadRequestException($"'{value}' is not an allowed value. Allowed: {candidates}.")
                .WithCode(SettingErrorCodes.ValueNotAllowed)
                .WithData("Value", value)
                .WithData("Allowed", candidates);
        }
    }
}
