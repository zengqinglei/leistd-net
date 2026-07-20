using System.Text.Json.Serialization;

namespace Leistd.Exception.AspNetCore;

/// <summary>
/// RFC 9457 / JSON:API 惯用的单条字段错误对象，作为 <c>errors</c> 数组的元素。
/// 一项同时承载展示与机器契约，避免拆成多个并列字段。
/// </summary>
/// <param name="Detail">本地化后的人类可读消息（对应 RFC 9457 <c>detail</c> / JSON:API <c>detail</c>）。</param>
/// <param name="Pointer">JSON Pointer，定位出错字段（如 <c>#/phone</c>）。对应 RFC 9457 <c>pointer</c>。</param>
/// <param name="Code">稳定机器错误码，前端可据此分支；可空，仅当抛出方设置时才有值。</param>
/// <param name="LocalizationKey">可改名的展示文案键；可空。</param>
public sealed record ErrorItem(
    [property: JsonPropertyName("detail")] string Detail,
    [property: JsonPropertyName("pointer")] string Pointer,
    [property: JsonPropertyName("code"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Code,
    [property: JsonPropertyName("localizationKey"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LocalizationKey);
