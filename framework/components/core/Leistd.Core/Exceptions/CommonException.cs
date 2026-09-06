namespace Leistd.Exceptions;

/// <summary>
/// 通用异常：不带 HTTP 语义的框架级异常基类，供不依赖 Web 栈的组件抛出。
/// </summary>
/// <remarks>
/// 接入 <c>Leistd.ExceptionHandling.AspNetCore</c> 时被归一化为 <c>BadRequestException</c>（400）。
/// 需要明确状态码语义请改用 <c>Leistd.ExceptionHandling.Core</c> 的 <c>BusinessException</c> 派生类。
/// </remarks>
/// <param name="message">面向日志与诊断的消息。</param>
/// <param name="innerException">内层异常。</param>
public class CommonException(string message, Exception? innerException = null) : Exception(message, innerException);
