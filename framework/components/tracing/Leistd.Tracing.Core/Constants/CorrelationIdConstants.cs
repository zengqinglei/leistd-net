namespace Leistd.Tracing.Constants;

/// <summary>
/// 关联标识的默认请求头、日志键与取值约束。
/// </summary>
public static class CorrelationIdConstants
{
    /// <summary>默认请求头名。</summary>
    public const string DefaultHeaderName = "X-Correlation-Id";

    /// <summary>
    /// 日志上下文中的关联标识键名。
    /// </summary>
    /// <remarks>
    /// 各服务无论用什么技术栈都写同一个键，运维才能一次查询命中整条业务链路。
    /// 链路追踪的 TraceId 由日志框架按 Activity 另行记录，与本键分开。
    /// </remarks>
    public const string LogKey = "leistd.correlationId";

    /// <summary>
    /// 采信入站关联标识的最大长度，与操作记录的落库列宽一致，保证记下的值能完整关联回日志。
    /// </summary>
    public const int MaxLength = 64;

    /// <summary>
    /// 判断一个外部传入的值能否作为关联标识：非空、不超过 <see cref="MaxLength"/>，
    /// 只含 ASCII 字母、数字、<c>-</c> 与 <c>_</c>。
    /// </summary>
    /// <remarks>它会进日志、响应头与下游请求，限制字符集防止日志与响应头注入。</remarks>
    public static bool IsWellFormed(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxLength)
        {
            return false;
        }

        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_')
            {
                return false;
            }
        }

        return true;
    }
}
