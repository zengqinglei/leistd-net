namespace Leistd.Exception.Core;

/// <summary>
/// 字段级验证错误（结构化）。与业务异常同构的三分离设计：
/// <see cref="Message"/> 英文诊断（进日志）/ <see cref="Code"/> 稳定机器契约 /
/// <see cref="LocalizationKey"/> 可改名的展示键，由异常处理器按当前 culture 解析为最终文案。
/// </summary>
/// <param name="Field">出错字段名（如 <c>Email</c>）。</param>
/// <param name="Message">英文诊断消息；无本地化键 / 键未命中时作为兜底展示文案。</param>
/// <param name="Code">稳定错误码（机器契约，前端可据此做特殊处理）；缺省 <c>null</c>。</param>
/// <param name="LocalizationKey">展示用本地化键；缺省 <c>null</c> 时直出 <see cref="Message"/>。</param>
/// <param name="Data">占位参数（键值），供本地化模板 <c>{name}</c> 填充；缺省 <c>null</c>。</param>
public sealed record ValidationError(
    string Field,
    string Message,
    string? Code = null,
    string? LocalizationKey = null,
    IReadOnlyDictionary<string, object?>? Data = null);
