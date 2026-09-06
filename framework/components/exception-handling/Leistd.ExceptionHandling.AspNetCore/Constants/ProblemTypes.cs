namespace Leistd.ExceptionHandling.AspNetCore.Constants;

/// <summary>
/// ProblemDetails 的问题类型（<c>type</c>）标识。
/// </summary>
/// <remarks>
/// 采用 <c>urn:</c> 非解析 URI（RFC 9457 §3.1.1 允许），不假定任何托管文档域名；
/// 也不用 <c>about:blank</c>——本框架每个错误响应都固定输出稳定的 <c>code</c>，已超出"仅 HTTP 状态码语义"。
/// 宿主若要改用可解析的自有文档 URI，可自行覆盖。
/// </remarks>
public static class ProblemTypes
{
    /// <summary>
    /// 校验错误的稳定问题类型；400（自动模型校验）与 422（业务校验异常）共用，标识两者相同的 <c>errors</c> 扩展契约。
    /// </summary>
    public const string ValidationError = "urn:leistd:problem:validation-error";

    /// <summary>
    /// 获取一般业务错误的稳定问题类型。
    /// </summary>
    public const string BusinessError = "urn:leistd:problem:business-error";
}
