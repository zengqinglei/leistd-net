#if (LocalIdentity)
namespace CompanyName.ProjectName.Application.Auth.Abstractions;

/// <summary>
/// 当前请求的客户端信息，登记登录会话时用来辨认设备。
/// </summary>
/// <remarks>应用层不引用 ASP.NET Core，由宿主从请求上下文提供；不在请求内时各项为 null。</remarks>
public interface IRequestClientInfo
{
    /// <summary>客户端 IP（经转发头还原后的）。</summary>
    string? IpAddress { get; }

    /// <summary>User-Agent 原文。</summary>
    string? UserAgent { get; }
}
#endif
