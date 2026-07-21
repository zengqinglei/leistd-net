using System.Text.Json.Serialization;

namespace Leistd.Exception.AspNetCore;

/// <summary>
/// <c>errors</c> 数组的单条字段错误对象——RFC 9457 Problem Details 的 Leistd 自定义扩展项。
/// 一项同时承载展示与机器契约，避免拆成多个并列字段。
/// </summary>
/// <remarks>
/// 属性名遵循宿主 ProblemDetails 序列化的命名策略（模板默认 camelCase → <c>detail</c>/<c>field</c>/<c>code</c>/
/// <c>localizationKey</c>），无需 <c>JsonPropertyName</c>；仅对可空的机器契约字段保留 <c>WhenWritingNull</c>，未设置时省略。
/// </remarks>
/// <param name="Detail">本地化后的人类可读消息。</param>
/// <param name="Field">出错字段路径（业务异常为 <c>ValidationError.Field</c>，自动模型校验为 MVC ModelState 键，如 <c>Address.Street</c>）。</param>
/// <param name="Code">稳定机器错误码，前端可据此分支；可空，仅当抛出方设置时才有值。Leistd 自定义扩展。</param>
/// <param name="LocalizationKey">可改名的展示文案键；可空。Leistd 自定义扩展。</param>
public sealed record ErrorItem(
    string Detail,
    string Field,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Code,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LocalizationKey);
