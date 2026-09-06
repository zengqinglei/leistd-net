namespace Leistd.ExceptionHandling;

/// <summary>
/// 表示身份认证无效，默认错误码为 <c>Error:Unauthorized</c>。
/// </summary>
public class UnauthorizedException(string message, Exception? innerException = null)
    : BusinessException(401, message, innerException);
