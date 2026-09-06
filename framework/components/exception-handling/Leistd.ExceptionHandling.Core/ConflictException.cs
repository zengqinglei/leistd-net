namespace Leistd.ExceptionHandling;

/// <summary>
/// 表示资源状态冲突，默认错误码为 <c>Error:Conflict</c>。
/// </summary>
public class ConflictException(string message, Exception? innerException = null)
    : BusinessException(409, message, innerException);
