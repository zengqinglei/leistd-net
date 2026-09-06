namespace Leistd.ExceptionHandling;

/// <summary>
/// 字段级验证错误（结构化）。与业务异常同构：<see cref="Message"/> 英文诊断（进日志）/
/// <see cref="Code"/> 既是机器契约也是展示词条键，由异常处理器按当前 culture 解析为最终文案。
/// </summary>
/// <remarks>
/// <see cref="Code"/> 可空且不设时从响应里省略——与顶层不同，字段错误没有"所属类别"可以推出默认码
/// （顶层用的是 HTTP 状态码）。不设时该项直出 <see cref="Message"/>。
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
