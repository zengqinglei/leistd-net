#if (ExternalLogin)
using Leistd.ExceptionHandling;
using Leistd.Lock;
using System.Security.Cryptography;
using System.Text;
using CompanyName.ProjectName.Application.Auth;
using CompanyName.ProjectName.Application.Auth.AppServices;
using CompanyName.ProjectName.Application.Auth.Dtos;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Leistd.Lock.Abstractions;

namespace CompanyName.ProjectName.Api.Controllers;

/// <summary>
/// 外部认证控制器
/// </summary>
[Route("api/v1/external-auth")]
public sealed class ExternalAuthController(
    IExternalAuthAppService externalAuthAppService,
    IDistributedCache distributedCache,
    IDistributedLock distributedLock,
    IHostEnvironment environment) : BaseController
{
    private const string StateCookieName = "__Host-CompanyName.ProjectName.ExternalAuth.State";
    private const string StateCacheKeyPrefix = "CompanyName.ProjectName:ExternalAuthState:";
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);

    /// <summary>
    /// 等待同键在飞解析的上限
    /// </summary>
    /// <remarks>
    /// 用 <c>TryLockAsync</c> 而不是无超时的 <c>LockAsync</c>：回调端点是匿名的，
    /// 而 state 由调用方提供，等于锁键由调用方决定。无界等待时，
    /// 攻击者用同一个 state 并发打进来就能把请求线程逐个挂住。
    /// 拿不到锁按无效 state 拒绝——重放请求本来就该被拒。
    /// </remarks>
    private static readonly TimeSpan StateLockTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// 获取外部登录 URL（GitHub, Google）
    /// </summary>
    [AllowAnonymous]
    [HttpGet("{provider}/login-url")]
    public async Task<ExternalLoginUrlOutputDto> GetLoginUrlAsync(
        string provider,
        CancellationToken cancellationToken)
    {
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var output = externalAuthAppService.GetLoginUrl(provider, state);
        await distributedCache.SetStringAsync(
            GetStateCacheKey(state),
            provider.ToLowerInvariant(),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = StateLifetime },
            cancellationToken);
        Response.Cookies.Append(StateCookieName, state, CreateStateCookieOptions());
        return output;
    }

    /// <summary>
    /// 处理外部登录回调
    /// </summary>
    [AllowAnonymous]
    [HttpPost("{provider}/callback")]
    [IgnoreAntiforgeryToken]
    public async Task CallbackAsync(
        string provider,
        [FromBody] ExternalLoginCallbackInputDto request,
        CancellationToken cancellationToken)
    {
        await ConsumeStateAsync(provider, request.State, cancellationToken);
        var principal = await externalAuthAppService.AuthenticateExternalUserAsync(
            provider,
            request,
            cancellationToken);

        await HttpContext.SignInAsync(AuthenticationSchemeNames.SessionCookie, principal,
            new AuthenticationProperties { IsPersistent = true });
    }

    private async Task ConsumeStateAsync(
        string provider,
        string state,
        CancellationToken cancellationToken)
    {
        Request.Cookies.TryGetValue(StateCookieName, out var cookieState);
        DeleteStateCookie();
        if (!FixedTimeEquals(cookieState, state))
        {
            throw InvalidState();
        }

        var cacheKey = GetStateCacheKey(state);
        await using var handle = await distributedLock.TryLockAsync(
            $"{cacheKey}:lock",
            StateLockTimeout,
            cancellationToken);
        if (handle is null)
        {
            // 同一个 state 已有一次消费在进行中——正常流程下不会发生，
            // 只可能是重放或并发提交，两者都该拒绝
            throw InvalidState();
        }

        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, handle.LockLost);
        var expectedProvider = await distributedCache.GetStringAsync(cacheKey, operation.Token);
        await distributedCache.RemoveAsync(cacheKey, operation.Token);
        if (!string.Equals(expectedProvider, provider, StringComparison.OrdinalIgnoreCase))
        {
            throw InvalidState();
        }
    }

    /// <summary>
    /// 状态 Cookie 的属性。<b>SameSite 必须与会话 Cookie 同策略</b>
    /// </summary>
    /// <remarks>
    /// <para>会话 Cookie 在生产用 <c>SameSite=None</c>，因为本模板支持前后端分离部署
    /// （SPA 与 API 不同源）。状态 Cookie 若固定为 <c>Lax</c>，那种部署下
    /// <c>GET login-url</c> 是跨站 XHR，浏览器<b>根本不会保存</b>它，
    /// 回调因此必然失败——而集成测试是手工把 Cookie 塞进请求头的，抓不到这一类问题。</para>
    /// <para><c>Secure</c> 不随环境放宽：<c>SameSite=None</c> 与 <c>__Host-</c> 前缀都强制要求它。
    /// 开发环境因此需要跑在 https 或 <c>localhost</c> 上（浏览器对 <c>http://localhost</c>
    /// 放行 Secure Cookie）——OAuth 提供商本来也只接受这两种回调地址。</para>
    /// </remarks>
    private CookieOptions CreateStateCookieOptions() => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = environment.IsDevelopment() ? SameSiteMode.Lax : SameSiteMode.None,
        IsEssential = true,
        Path = "/",
        MaxAge = StateLifetime
    };

    /// <summary>
    /// 清除状态 Cookie
    /// </summary>
    /// <remarks>
    /// 不复用 <see cref="CreateStateCookieOptions"/>：<c>Delete</c> 会原样拷贝传入的
    /// <c>MaxAge</c> 再只覆盖 <c>Expires</c>，而按 RFC 6265 §5.2.2 <c>Max-Age</c> 优先，
    /// 结果是"删除"反而把 Cookie 又留了 10 分钟。属性仍需与写入时一致，否则浏览器不认同一条。
    /// </remarks>
    private void DeleteStateCookie()
    {
        var options = CreateStateCookieOptions();
        options.MaxAge = null;
        Response.Cookies.Delete(StateCookieName, options);
    }

    private static string GetStateCacheKey(string state)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(state));
        return $"{StateCacheKeyPrefix}{Convert.ToHexString(digest)}";
    }

    private static bool FixedTimeEquals(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(left),
            Encoding.UTF8.GetBytes(right));
    }

    private static BadRequestException InvalidState() =>
        new BadRequestException("Invalid or expired external authentication state.")
#if (IncludeLocalization)
            .WithCode("ExternalAuth:InvalidState")
#endif
            ;
}
#endif
