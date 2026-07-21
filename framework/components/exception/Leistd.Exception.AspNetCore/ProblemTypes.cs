namespace Leistd.Exception.AspNetCore;

/// <summary>
/// ProblemDetails 的问题类型（<c>type</c>）标识。
/// </summary>
/// <remarks>
/// 采用 <c>urn:</c> 非解析 URI（RFC 9457 §3.1.1 明确允许非解析 type URI）——框架/模板不假定任何托管文档域名，
/// 也不用 <c>about:blank</c>：本框架的每个错误响应都固定输出稳定机器契约（验证错误另有 <c>errors</c> 数组，所有错误都带
/// 可供客户端分支的 <c>code</c>），已超出「仅 HTTP 状态码语义」；据 RFC 9457 §4.2.1，<c>about:blank</c> 表示「无 HTTP
/// 状态码之外的额外语义」，与固定发出的稳定 <c>code</c> 相矛盾，故所有错误都给出定义过的 type，不用 <c>about:blank</c>。
/// 宿主若要改用可解析的自有文档 URI，可自行覆盖。
/// </remarks>
public static class ProblemTypes
{
    /// <summary>
    /// 校验错误的稳定问题类型；400（自动模型校验）与 422（业务校验异常）共用，标识两者相同的 <c>errors</c> 扩展契约。
    /// </summary>
    public const string ValidationError = "urn:leistd:problem:validation-error";

    /// <summary>
    /// 一般业务错误的稳定问题类型（非校验类，如 404/409/503 等）。这些响应仍固定输出稳定 <c>code</c> 机器契约，
    /// 超出「仅 HTTP 状态码语义」，据 RFC 9457 §4.2.1 不应标 <c>about:blank</c>，故统一给出此定义过的 type。
    /// </summary>
    public const string BusinessError = "urn:leistd:problem:business-error";
}
