#if (LocalIdentity)
using CompanyName.ProjectName.Application.Auth.Abstractions;

namespace CompanyName.ProjectName.Api.Auth;

/// <summary>
/// 从当前 HTTP 请求读取客户端信息。IP 取转发头还原后的值（管道最前面的 <c>UseForwardedHeaders</c>）。
/// </summary>
public sealed class HttpRequestClientInfo(IHttpContextAccessor httpContextAccessor) : IRequestClientInfo
{
    /// <inheritdoc />
    public string? IpAddress => httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    /// <inheritdoc />
    public string? UserAgent
    {
        get
        {
            var value = httpContextAccessor.HttpContext?.Request.Headers.UserAgent.ToString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }
}
#endif
