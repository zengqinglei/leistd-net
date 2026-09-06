#if (RemoteTokenAuth)
namespace CompanyName.ProjectName.Api.Options;

/// <summary>
/// 远端签发方配置（配置节 <c>Authentication</c>）
/// </summary>
/// <remarks>
/// <para>"什么样的签发方配置算可用"只在这里定义一次。它有两个消费点：
/// <c>Program.cs</c> 组合期要拿 issuer 去配 OpenIddict 校验器，
/// <c>RemoteIdentityReadinessInitializer</c> 运行期要拿它探发现文档。
/// 两处各自读一遍原始配置键、各自判一次空的话，规则就有两个版本。</para>
/// <para>校验同时挂 <c>ValidateOnStart</c>：组合期读到的配置还不是最终值
/// （集成测试通过 <c>WebApplicationFactory</c> 追加的覆盖此刻尚未合入），
/// 而配置定案后、接流量之前必须再确认一次。</para>
/// </remarks>
internal sealed class RemoteIdentityOptions
{
    /// <summary>配置节名</summary>
    public const string SectionName = "Authentication";

    /// <summary>签发方地址，必须是绝对 http(s) URI</summary>
    public string? Issuer { get; set; }

    /// <summary>本服务在令牌 <c>aud</c> 中的标识</summary>
    public string? Audience { get; set; }

    /// <summary>解析后的签发方地址；不是合法绝对 http(s) URI 时为 <see langword="null"/></summary>
    public Uri? IssuerUri =>
        Uri.TryCreate(Issuer, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri
            : null;

    /// <summary>配置是否可用</summary>
    public bool IsUsable => IssuerUri is not null && !string.IsNullOrWhiteSpace(Audience);

    /// <summary>
    /// 发现文档地址
    /// </summary>
    /// <remarks>仅在 <see cref="IsUsable"/> 成立后取用；启动期校验保证了这一点。</remarks>
    public Uri MetadataUrl => new(IssuerUri!, ".well-known/openid-configuration");
}
#endif
