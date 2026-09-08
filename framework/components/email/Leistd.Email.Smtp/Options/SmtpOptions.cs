namespace Leistd.Email.Smtp.Options;

/// <summary>
/// 配置 SMTP 发信。
/// </summary>
/// <remarks>
/// 全部取值在宿主启动时校验（<c>ValidateOnStart</c>）：一个没配好 SMTP 的部署应当在接流量
/// 之前失败，而不是在第一个用户注册时才失败。
/// </remarks>
public sealed class SmtpOptions
{
    /// <summary>获取配置节名称 <c>Leistd:Email:Smtp</c>。</summary>
    public const string SectionName = "Leistd:Email:Smtp";

    /// <summary>获取或设置 SMTP 主机。</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>获取或设置 SMTP 端口；默认 587。</summary>
    public int Port { get; set; } = 587;

    /// <summary>获取或设置登录用户名；为空表示匿名投递。</summary>
    /// <remarks>
    /// 与 <see cref="Password"/> 必须同时给出或同时留空。只给一个会让"以为在认证、其实没有"
    /// 成为默认行为——服务器可能按匿名中继接受，也可能直接拒收，两种都不在配置阶段暴露。
    /// </remarks>
    public string? Username { get; set; }

    /// <summary>获取或设置登录口令；语义见 <see cref="Username"/>。</summary>
    /// <remarks>
    /// <b>不要写进随代码分发的配置文件。</b>用环境变量、用户机密或密钥管理服务注入。
    /// </remarks>
    public string? Password { get; set; }

    /// <summary>获取或设置是否启用传输层加密；默认 <see langword="true"/>。</summary>
    /// <remarks>
    /// 启用时按端口选择握手方式：465 用连接即 TLS（隐式 TLS），其余端口用 STARTTLS。
    /// 关闭后凭据与正文以明文过网，仅适用于本机的开发用收信服务。
    /// </remarks>
    public bool EnableSsl { get; set; } = true;

    /// <summary>获取或设置默认发件地址。</summary>
    /// <remarks><see cref="Abstractions.EmailMessage.FromAddress"/> 未指定时使用本值。</remarks>
    public string DefaultFromAddress { get; set; } = string.Empty;

    /// <summary>获取或设置默认发件显示名。</summary>
    public string? DefaultFromName { get; set; }
}
