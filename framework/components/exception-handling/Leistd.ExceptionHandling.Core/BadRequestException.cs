namespace Leistd.ExceptionHandling;

/// <summary>
/// 表示请求无效，默认错误码为 <c>Error:BadRequest</c>。
/// </summary>
public class BadRequestException(string message, Exception? innerException = null)
    : BusinessException(400, message, innerException);
