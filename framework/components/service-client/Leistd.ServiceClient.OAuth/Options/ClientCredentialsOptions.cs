namespace Leistd.ServiceClient.OAuth.Options;

/// <summary>
/// 配置 OAuth 2.0 client credentials 认证。
/// </summary>
/// <remarks>服务调用身份全局共享，目标服务可单独覆盖申请范围。</remarks>
public class ClientCredentialsOptions
{
    /// <summary>
    /// 身份服务基础地址（如 <c>http://identity-service</c>）。
    /// token 端点默认为 <c>{Authority}/connect/token</c>。
    /// </summary>
    public string? Authority { get; set; }

    /// <summary>
    /// token 端点完整地址；设置后覆盖 <see cref="Authority"/> 推导的默认值。
    /// </summary>
    public string? TokenEndpoint { get; set; }

    /// <summary>
    /// 客户端标识（在身份服务注册的 client_id）。
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// 客户端密钥。应来自密钥管理，不要写入源码或提交到仓库的配置文件。
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// 申请的 scope（可空；多个用空格分隔）。
    /// </summary>
    public string? Scope { get; set; }

    /// <summary>
    /// 过期缓冲：token 剩余有效期不足该值时视为过期并提前刷新。默认 60 秒。
    /// </summary>
    public TimeSpan ExpirationBuffer { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// 解析生效的 token 端点地址。
    /// </summary>
    /// <exception cref="InvalidOperationException"><see cref="TokenEndpoint"/> 与 <see cref="Authority"/> 均未配置</exception>
    public string ResolveTokenEndpoint()
    {
        if (!string.IsNullOrWhiteSpace(TokenEndpoint))
        {
            return TokenEndpoint;
        }

        if (!string.IsNullOrWhiteSpace(Authority))
        {
            return $"{Authority.TrimEnd('/')}/connect/token";
        }

        throw new InvalidOperationException("ClientCredentialsOptions requires either Authority or TokenEndpoint to be configured.");
    }
}
