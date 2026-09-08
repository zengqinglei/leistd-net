namespace Leistd.ExceptionHandling;

/// <summary>
/// 表示服务暂时不可用，默认错误码为 <c>Error:ServiceUnavailable</c>。
/// </summary>
public class ServiceUnavailableException(string message, Exception? innerException = null)
    : BusinessException(503, message, innerException);
