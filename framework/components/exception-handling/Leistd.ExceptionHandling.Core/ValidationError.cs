namespace Leistd.ExceptionHandling;

/// <summary>
/// 携带字段路径、诊断消息与可选本地化信息的验证错误。
/// </summary>
/// <remarks>
/// <see cref="Code"/> 未设置或词条未命中时，字段错误直接使用 <see cref="Message"/>；
/// 与顶层异常的通用状态消息回退不同。
/// </remarks>
/// <param name="Field">出错字段名（如 <c>Email</c>）。</param>
/// <param name="Message">英文诊断消息；无错误码 / 词条未命中时作为兜底展示文案。</param>
/// <param name="Code">错误码，同时是展示词条键（机器契约，前端可据此分支）；缺省 <c>null</c>。</param>
/// <param name="Data">占位参数（键值），供本地化模板 <c>{name}</c> 填充；缺省 <c>null</c>。</param>
public sealed record ValidationError(
    string Field,
    string Message,
    string? Code = null,
    IReadOnlyDictionary<string, object?>? Data = null);
