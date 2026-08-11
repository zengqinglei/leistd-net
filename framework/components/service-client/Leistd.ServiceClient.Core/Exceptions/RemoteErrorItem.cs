namespace Leistd.ServiceClient.Exceptions;

/// <summary>
/// 远端错误响应中的单条字段级错误（对应 Leistd 全局异常处理器 ProblemDetails 的 <c>errors</c> 项）。
/// </summary>
/// <param name="Field">出错字段名（可空）</param>
/// <param name="Detail">错误描述</param>
/// <param name="Code">稳定机器码（可空）</param>
public sealed record RemoteErrorItem(string? Field, string? Detail, string? Code);
