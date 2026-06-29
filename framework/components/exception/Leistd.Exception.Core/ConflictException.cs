namespace Leistd.Exception.Core;

/// <summary>
/// 资源冲突异常，默认错误码：40900
/// </summary>
public class ConflictException(string message, System.Exception? innerException = null)
    : BusinessException("409", message, innerException);
