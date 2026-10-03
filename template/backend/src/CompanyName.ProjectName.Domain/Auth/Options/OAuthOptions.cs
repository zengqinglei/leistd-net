#if (LocalIdentity)
namespace CompanyName.ProjectName.Domain.Auth.Options;

public class OAuthOptions
{
    public const string SectionName = "OAuth";

    /// <summary>
    /// 是否使用 OpenIddict 的开发证书签名与加密令牌。默认关闭，只在开发配置里打开。
    /// </summary>
    /// <remarks>
    /// 开发证书生成在本机证书存储里，每台机器、每个容器各一份：多副本之间互不认，重建容器后已签发的令牌全部失效。
    /// 关闭时 <see cref="SigningCertificates"/> 与 <see cref="EncryptionCertificates"/> 都至少要有一张，否则启动失败。
    /// </remarks>
    public bool UseDevelopmentCertificates { get; set; }

    /// <summary>
    /// 令牌签名证书。全部发布进 JWKS；签名时 OpenIddict 优先用已生效且到期最晚的一张，尚未生效的排在后面（启动时排定）。
    /// </summary>
    /// <remarks>
    /// 轮换时同时登记新旧两张（重叠期），顺序见部署文档：先发布新公钥、确认各资源服务已取得，再让新证书生效。
    /// </remarks>
    public OAuthCertificate[] SigningCertificates { get; set; } = [];

    /// <summary>
    /// 令牌加密证书（授权码、刷新令牌等只给本服务读的令牌）。撤掉旧证书前，用它加密的令牌都要已经过期。
    /// </summary>
    public OAuthCertificate[] EncryptionCertificates { get; set; } = [];

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
    /// 本服务 API 的资源标识（访问令牌的受众）。同名登记为 scope：客户端申请它，签发的令牌才能调用本服务的 API。
    /// </summary>
    public string Resource { get; set; } = "companyname-projectname-api";

    /// <summary>
    /// 下游 API 的资源、scope 与拥有该资源的客户端。未指定 scope 或所有者时取资源名。
    /// </summary>
    /// <remarks>下游服务把自己的 <c>Authentication:Audience</c> 设为同一个值。不能为空、不能重复，也不能与内置 scope 同名。</remarks>
    public OAuthApiResource[] ApiResources { get; set; } = [];
}

/// <summary>一张 PKCS#12 证书文件及其口令（口令由部署注入，不写进配置文件）。</summary>
public sealed class OAuthCertificate
{
    public string Path { get; set; } = string.Empty;
    public string? Password { get; set; }
}

/// <summary>一个 API 资源的申请范围和交换发起方归属。</summary>
public sealed class OAuthApiResource
{
    public string Name { get; set; } = string.Empty;
    public string? Scope { get; set; }
    public string? OwnerClientId { get; set; }
    public string ScopeName => Scope ?? Name;
    public string Owner => OwnerClientId ?? Name;
}
#endif
