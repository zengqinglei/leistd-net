namespace Leistd.ExceptionHandling;

/// <summary>
/// 表示访问被拒绝，默认错误码为 <c>Error:Forbidden</c>。
/// </summary>
public class ForbiddenException(string message, Exception? innerException = null)
    : BusinessException(403, message, innerException);
