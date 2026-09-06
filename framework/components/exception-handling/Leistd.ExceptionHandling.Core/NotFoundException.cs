namespace Leistd.ExceptionHandling;

/// <summary>
/// 表示资源不存在，默认错误码为 <c>Error:NotFound</c>。
/// </summary>
public class NotFoundException(string message, Exception? innerException = null)
    : BusinessException(404, message, innerException);
