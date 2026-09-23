namespace Leistd.ExceptionHandling;

/// <summary>
/// 表示可预期且允许向调用方公开的业务失败。
/// </summary>
/// <remarks>
/// <see cref="Code"/> 是稳定的机器契约与本地化键；<see cref="Exception.Message"/>
/// 是词条缺失或未启用本地化时可安全展示的默认文案。本类不携带 HTTP 状态码，
/// 传输边界根据错误码或宿主映射决定响应状态。
/// </remarks>
public class BusinessException : Exception
{
    /// <summary>
    /// 错误码：既是对外的稳定机器契约，也是本地化资源的查找键（如 <c>User:EmailAlreadyUsed</c>）
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// 本地化占位参数，供资源中的具名占位符（如 <c>{Sku}</c>）填充；未启用本地化时忽略。
    /// </summary>
    public IReadOnlyDictionary<string, object?> LocalizationData => _localizationData;

    private readonly Dictionary<string, object?> _localizationData = new(StringComparer.Ordinal);

    /// <summary>以稳定错误码与安全默认文案构造业务异常。</summary>
    /// <param name="code">错误码兼本地化资源键。</param>
    /// <param name="message">词条缺失时允许向调用方展示的安全默认文案。</param>
    /// <param name="innerException">内层异常。</param>
    public BusinessException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Code = code;
    }

    /// <summary>
    /// 追加一个本地化占位参数，供资源中的具名占位符填充。
    /// </summary>
    /// <param name="name">占位符名，与资源中的 <c>{Name}</c> 对应。</param>
    /// <param name="value">填充值。</param>
    public BusinessException WithData(string name, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _localizationData[name] = value;
        return this;
    }
}
