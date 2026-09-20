using Leistd.Settings.Abstractions;
using Leistd.Settings.Definitions;

namespace Leistd.Settings.Validation;

/// <summary>
/// 业务取值校验：定义上的值元数据表达不了的规则（时区标识、邮箱地址、开启前提）。
/// </summary>
/// <remarks>
/// <para>在 <see cref="ISettingManager"/> 写入前、通过类型、区间与候选校验之后依次调用，
/// 清除（值为 <see langword="null"/>）不经过它。每个校验器自己按设置名判断是否适用。</para>
/// <para>不合法时抛带码的业务异常（通常是 <c>BadRequestException(...).WithCode(...)</c>），写入随之中止。</para>
/// </remarks>
/// <example>
/// <code>
/// internal sealed class TimeZoneSettingValidator : ISettingValueValidator
/// {
///     public Task ValidateAsync(SettingValueValidationContext context, CancellationToken cancellationToken = default)
///     {
///         if (context.Definition.Name == "Display.TimeZone"
///             &amp;&amp; !(TimeZoneInfo.TryFindSystemTimeZoneById(context.Value, out var zone) &amp;&amp; zone.HasIanaId))
///             throw new BadRequestException($"'{context.Value}' is not an IANA time zone id.").WithCode("Setting:TimeZoneInvalid");
///         return Task.CompletedTask;
///     }
/// }
///
/// builder.Services.AddTransient&lt;ISettingValueValidator, TimeZoneSettingValidator&gt;();
/// </code>
/// </example>
public interface ISettingValueValidator
{
    /// <summary>校验一次写入；不合法时抛出。</summary>
    /// <param name="context">写入内容。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task ValidateAsync(SettingValueValidationContext context, CancellationToken cancellationToken = default);
}

/// <summary>一次待校验的写入。</summary>
/// <param name="Definition">设置定义。</param>
/// <param name="Value">要写入的值，非空。</param>
/// <param name="Scope">写入层级。</param>
/// <param name="UserId">用户级时的用户标识。</param>
public sealed record SettingValueValidationContext(
    ISettingDefinition Definition,
    string Value,
    SettingScopes Scope,
    string? UserId);
