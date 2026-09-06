namespace Leistd.ExceptionHandling;

/// <summary>
/// 表示媒体类型不受支持，默认错误码为 <c>Error:UnsupportedMediaType</c>。
/// </summary>
public class UnsupportedMediaTypeException(string message, Exception? innerException = null)
    : BusinessException(415, message, innerException);
