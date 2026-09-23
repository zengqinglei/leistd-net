#if (LocalIdentity)
namespace CompanyName.ProjectName.Domain.Auth.Options;

public class OAuthOptions
{
    public const string SectionName = "OAuth";



    /// <summary>
    /// Cookie 会话过期天数（默认7天）
    /// </summary>
    public int CookieExpireDays { get; set; } = 7;

    /// <summary>
    /// 是否使用 OpenIddict 的开发证书签名与加密令牌。默认关闭，只在开发配置里打开。
    /// </summary>
    /// <remarks>
    /// 开发证书生成在本机证书存储里，每台机器、每个容器各一份：多副本之间互不认，重建容器后已签发的令牌全部失效。
    /// 关闭时必须提供 <see cref="SigningCertificatePath"/> 与 <see cref="EncryptionCertificatePath"/>，否则启动失败。
    /// </remarks>
    public bool UseDevelopmentCertificates { get; set; }

    public string? SigningCertificatePath { get; set; }

    public string? SigningCertificatePassword { get; set; }

    public string? EncryptionCertificatePath { get; set; }

    public string? EncryptionCertificatePassword { get; set; }

    /// <summary>
    /// 是否关闭 OpenIddict 的传输安全（HTTPS）要求。只用于本机或测试宿主的纯 HTTP 调试，生产环境不要打开。
    /// </summary>
    /// <remarks>
    /// 部署在做 TLS 终结的网关或 ingress 后面时，OpenIddict 拒绝请求说明应用没有还原原始协议：
    /// 配置 <c>ForwardedHeaders:KnownProxies</c> / <c>KnownNetworks</c> 信任代理，而不是关闭这项检查。
    /// </remarks>
    public bool DisableHttpsRequirement { get; set; } = false;

    /// <summary>
    /// OpenIddict 签发 token 使用的 issuer URL（外部可访问的域名）。
    /// 为空时使用请求的 host。跨服务 token 验证场景必须设置。
    /// </summary>
    public string? Issuer { get; set; }

    /// <summary>
    /// OpenIddict 资源标识符（API audience）
    /// </summary>
    public string Resource { get; set; } = "companyname-projectname-api";
}
#endif
