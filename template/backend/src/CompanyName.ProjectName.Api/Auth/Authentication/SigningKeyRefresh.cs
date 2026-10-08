using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using OpenIddict.Abstractions;
using OpenIddict.Validation;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Validation.OpenIddictValidationEvents;
using static OpenIddict.Validation.OpenIddictValidationHandlers;

namespace CompanyName.ProjectName.Api.Auth.Authentication;

/// <summary>Identity 轮换签名证书后，资源服务在同一个请求里取回新公钥，而不是先拒绝、下一次才认得。</summary>
/// <remarks>
/// <para>访问令牌（Bearer、登录回调、服务端续期）由 OpenIddict 验证：遇到不认识的 kid，官方处理器只请求刷新配置就拒绝，
/// 刷新要到之后的请求才用得上。这里排在官方验签之前：kid 不在本次的验签密钥里时，向<b>配置的签发方</b>刷新一次，
/// 把刷新得到的公钥放进本次（克隆的）验签参数，再交回官方处理器——签名、issuer、audience、有效期仍由它判定。</para>
/// <para>只处理可读的 JWS：已有主体、非 JWT、加密令牌（外层 kid 是解密密钥）、没有 kid 的一律跳过。
/// 公钥只来自配置的发现文档与 JWKS，不使用令牌里的 iss、jku、x5u。刷新结果替换本次参数里的动态公钥（静态登记的保留），
/// 签发方撤掉的旧公钥不会因此在本次请求里继续被接受（命中与否都替换）。逐请求结果只记 Debug，真实刷新尝试由限频器记 Information。</para>
/// <para>取配置依赖进程级开关 <c>Switch.Microsoft.IdentityModel.UpdateConfigAsBlocking</c>（宿主项目以
/// <c>RuntimeHostConfigurationOption</c> 设置）：不开时请求刷新只会触发后台更新，本次拿到的仍是旧配置。
/// 开关同样作用于定期自动刷新，到点时正常请求也会等这一次抓取；抓取的传输超时见 <see cref="FetchTimeout"/>。</para>
/// <para>id_token 只在登录回调里由 ASP.NET Core OIDC 处理器经后通道从签发方取得、不经浏览器，验签失败时
/// IdentityModel 自身会刷新并重试一次（同一开关）；攻击者送不进任意 kid，所以那条链路不另加限频。</para>
/// </remarks>
public sealed class RefreshSigningKeysOnUnknownKeyIdentifier(ILogger<RefreshSigningKeysOnUnknownKeyIdentifier> logger)
    : IOpenIddictValidationHandler<ValidateTokenContext>
{
    /// <summary>抓取发现文档与 JWKS 的单次 HTTP 超时（含官方重试），也是本处理器等待刷新的上限。</summary>
    public static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(10);

    public static OpenIddictValidationHandlerDescriptor Descriptor { get; }
        = OpenIddictValidationHandlerDescriptor.CreateBuilder<ValidateTokenContext>()
            .UseSingletonHandler<RefreshSigningKeysOnUnknownKeyIdentifier>()
            // 必须在官方验签之前：它一旦拒绝，分发即停止，之后没有机会补救
            .SetOrder(Protection.ValidateIdentityModelToken.Descriptor.Order - 1)
            .SetType(OpenIddictValidationHandlerType.Custom)
            .Build();

    public async ValueTask HandleAsync(ValidateTokenContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Principal is not null || context.TokenValidationParameters is not { } parameters ||
            context.TokenFormat is not null and not TokenFormats.Private.JsonWebToken ||
            string.IsNullOrEmpty(context.Token) || !context.SecurityTokenHandler.CanReadToken(context.Token))
            return;

        string keyId;
        try
        {
            var token = new JsonWebToken(context.Token);
            if (token.IsEncrypted || string.IsNullOrEmpty(token.Kid)) return;
            keyId = token.Kid;
        }
        catch (ArgumentException)
        {
            return;
        }

        if (parameters.IssuerSigningKeys?.Any(key => string.Equals(key.KeyId, keyId, StringComparison.Ordinal)) == true)
            return;

        var manager = context.Options.ConfigurationManager;
        OpenIddictConfiguration configuration;
        try
        {
            // 请求刷新经 SigningKeyRefreshThrottle 限频；同一时刻的多次刷新由官方管理器合并成一次抓取
            manager.RequestRefresh();
            configuration = await manager.GetConfigurationAsync(context.CancellationToken)
                .WaitAsync(FetchTimeout, context.CancellationToken);
        }
        catch (Exception exception) when (exception is TimeoutException or InvalidOperationException or HttpRequestException)
        {
            // 取不到不改变结论：交官方处理器按当前公钥判定（多半拒绝）。逐请求的结果只记 Debug——
            // 匿名请求可以任意制造未知 kid，按请求记高等级日志等于让它放大日志量；真实刷新尝试由限频器记录
            logger.LogDebug("Refreshing the signing keys for an unknown key identifier failed ({ExceptionType}).",
                exception.GetType().Name);
            return;
        }

        // 动态公钥以返回的配置为准（静态登记的保留）：命中与否都替换，签发方已撤掉的旧公钥不能在本次请求里继续参与验签
        // （官方默认会尝试全部候选公钥）。抓取失败时管理器返回的就是原配置，替换不改变什么。
        var refreshed = configuration.SigningKeys;
        parameters.IssuerSigningKeys = (context.Options.TokenValidationParameters.IssuerSigningKeys ?? []).Concat(refreshed).ToArray();
        logger.LogDebug("Signing keys checked for an unknown key identifier; the key was {Result} at the issuer.",
            refreshed.Any(key => string.Equals(key.KeyId, keyId, StringComparison.Ordinal)) ? "found" : "not found");
    }
}

/// <summary>给 OpenIddict 验证的配置管理器限频：请求刷新在 <see cref="MinimumInterval"/> 内只转交一次。</summary>
/// <remarks>
/// IdentityModel 的阻塞模式在抓取失败时保留旧配置，但距上次<b>成功</b>刷新超过 RefreshInterval 后，每次请求刷新都会把
/// 下次同步时间拨回现在——签发方不可用时，带伪造 kid 的请求（官方处理器验签失败也会请求刷新）就能让每个请求都去抓一次。
/// 限频放在管理器上，同时约束官方与 <see cref="RefreshSigningKeysOnUnknownKeyIdentifier"/> 的请求；定期自动刷新不受影响。
/// </remarks>
public sealed class SigningKeyRefreshThrottle(
    IConfigurationManager<OpenIddictConfiguration> inner, TimeProvider time, ILogger<SigningKeyRefreshThrottle> logger)
    : IConfigurationManager<OpenIddictConfiguration>
{
    /// <summary>两次转交请求刷新之间的最短间隔（每个副本各自计算）。</summary>
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromMinutes(1);

    private readonly Lock _gate = new();
    private DateTimeOffset _nextRefresh = DateTimeOffset.MinValue;

    public Task<OpenIddictConfiguration> GetConfigurationAsync(CancellationToken cancel) => inner.GetConfigurationAsync(cancel);

    public void RequestRefresh()
    {
        lock (_gate)
        {
            var now = time.GetUtcNow();
            if (now < _nextRefresh) return;
            _nextRefresh = now + MinimumInterval;
            // 在锁内转交：并发请求被判为"已限频"时，内层必须已经标记需要刷新，
            // 否则它在这个空档里取配置会拿到旧公钥，而不是等这次抓取
            inner.RequestRefresh();
        }
        // 每个副本每个间隔至多一条：运维据此看到真实的刷新尝试，而不是被伪造 kid 的请求数淹没
        logger.LogInformation("Requested a signing key refresh from the configured issuer.");
    }
}

/// <summary>在 OpenIddict 建好配置管理器之后，把它包进 <see cref="SigningKeyRefreshThrottle"/>。</summary>
public sealed class ThrottleSigningKeyRefresh(TimeProvider time, ILogger<SigningKeyRefreshThrottle> logger)
    : IPostConfigureOptions<OpenIddictValidationOptions>
{
    public void PostConfigure(string? name, OpenIddictValidationOptions options)
    {
        if (options.ConfigurationManager is not null and not SigningKeyRefreshThrottle)
            options.ConfigurationManager = new SigningKeyRefreshThrottle(options.ConfigurationManager, time, logger);
    }
}
