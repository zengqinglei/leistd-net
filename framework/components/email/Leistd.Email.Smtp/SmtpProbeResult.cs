namespace Leistd.Email.Smtp;

/// <summary>SMTP 连通探测的结果。</summary>
public enum SmtpProbeResult
{
    /// <summary>连接成功；配置了用户名时认证也通过。</summary>
    Succeeded,

    /// <summary>连上了，但服务器拒绝了用户名或口令。</summary>
    AuthenticationRejected,

    /// <summary>连不上：DNS、连接、TLS 握手、协议应答或超时。</summary>
    Unreachable,
}
