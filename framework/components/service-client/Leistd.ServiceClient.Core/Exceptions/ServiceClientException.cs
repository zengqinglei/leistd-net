using Leistd.ExceptionHandling;

namespace Leistd.ServiceClient.Exceptions;

/// <summary>
/// 表示服务调用的传输或响应解析失败。
/// </summary>
/// <remarks>
/// 默认状态码为 HTTP 503；派生类可通过构造函数指定其他状态码。
/// 是否重试由调用方根据操作幂等性与失败情况决定。
/// 远端明确返回的错误使用 <see cref="RemoteServiceException"/>。
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
