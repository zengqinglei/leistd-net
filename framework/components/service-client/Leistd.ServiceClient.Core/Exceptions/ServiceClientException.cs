using Leistd.ExceptionHandling;

namespace Leistd.ServiceClient.Exceptions;

/// <summary>
/// 服务调用客户端异常：网络失败、超时、响应反序列化失败等<b>未到达远端业务逻辑</b>
/// 或<b>无法解析远端结果</b>的错误。远端明确返回的业务错误用其派生类
/// <see cref="RemoteServiceException"/> 表达。
/// </summary>
/// <remarks>
/// <para><b>映射为 HTTP 503</b>（可重试）：上游连不上、超时、响应无法解析都是上游故障，
/// 不是调用方请求有误，503 的可重试语义也让调用方的重试策略成立。</para>
/// <para>对外状态码由构造函数决定，派生类可覆盖——见 <see cref="RemoteServiceException"/>。</para>
/// </remarks>
public class ServiceClientException : BusinessException
{
    /// <summary>
    /// 创建服务调用异常（映射为 503）。
    /// </summary>
    /// <param name="message">异常消息（含 HTTP 方法、URL 等请求要素）</param>
    /// <param name="innerException">原始异常</param>
    public ServiceClientException(string message, Exception? innerException = null)
        : base(503, message, innerException)
    {
    }

    /// <summary>
    /// 供派生类指定对外状态码。
    /// </summary>
    /// <param name="statusCode">对外 HTTP 状态码，如 <c>502</c></param>
    /// <param name="message">异常消息</param>
    /// <param name="innerException">原始异常</param>
    protected ServiceClientException(int statusCode, string message, Exception? innerException = null)
        : base(statusCode, message, innerException)
    {
    }
}
