using Leistd.Exception;

namespace Leistd.ServiceClient.Exceptions;

/// <summary>
/// 服务调用客户端异常：网络失败、超时、响应反序列化失败等**未到达远端业务逻辑**
/// 或**无法解析远端结果**的错误。远端明确返回的业务错误用其派生类
/// <see cref="RemoteServiceException"/> 表达。
/// </summary>
/// <param name="message">异常消息（含 HTTP 方法、URL 等请求要素）</param>
/// <param name="innerException">原始异常</param>
public class ServiceClientException(string message, System.Exception? innerException = null)
    : CommonException(message, innerException);
