namespace Leistd.ExceptionHandling;

/// <summary>
/// 表示服务器内部错误，默认错误码为 <c>Error:InternalServer</c>。
/// </summary>
public class InternalServerException(string message, Exception? innerException = null)
    : BusinessException(500, message, innerException);
