#if (LocalIdentity)
using System.Security.Claims;
using CompanyName.ProjectName.Application.Auth.AppServices;
using CompanyName.ProjectName.Application.Auth.SignIn;
using CompanyName.ProjectName.Domain.Auth.Errors;
using Leistd.ExceptionHandling;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace CompanyName.ProjectName.Api.Auth.Sessions;

/// <summary>待完成登录（第一步已通过、尚待第二步）的临时 Cookie：官方两步登录的 <see cref="IdentityConstants.TwoFactorUserIdScheme"/> 方案。</summary>
/// <remarks>
/// <para>票据只放挑战引用；次数、安全版本、一次消费与凭据锁都在服务端挑战记录里，Cookie 有效而记录不在即视为失效。
/// 绝对期限等于挑战到期，不持久、不滑动。它不是正式身份：不进默认方案与 Smart 选择器，只有第二步端点与授权端点显式认证它。</para>
/// <para>官方 Cookie 处理器对过期或篡改的票据只返回失败、不删 Cookie，因此读到失败时在这里签出；
/// 新的待完成登录先签出旧的（并作废旧挑战）再签入，第二步通过时先签出再签发会话，顺序与 <c>SignInManager</c> 相同。</para>
/// </remarks>
public sealed class PendingSignInCookie(IAuthAppService authService)
{
    /// <summary>临时 Cookie 的认证方案名，取官方常量。</summary>
    public static readonly string Scheme = IdentityConstants.TwoFactorUserIdScheme;

    private const string ChallengeClaimType = "two_factor_challenge";

    /// <summary>签入一次新的待完成登录：先结束当前请求带来的旧的（作废旧挑战、删旧 Cookie），再写入新的挑战引用。</summary>
    public async Task SignInAsync(HttpContext httpContext, TwoFactorPrompt prompt, CancellationToken cancellationToken)
    {
        await EndAsync(httpContext, cancellationToken);
        var identity = new ClaimsIdentity(Scheme);
        identity.AddClaim(new Claim(ChallengeClaimType, prompt.Token));
        await httpContext.SignInAsync(Scheme, new ClaimsPrincipal(identity), new AuthenticationProperties
        {
            IsPersistent = false,
            AllowRefresh = false,
            ExpiresUtc = new DateTimeOffset(DateTime.SpecifyKind(prompt.ExpiresAt, DateTimeKind.Utc))
        });
    }

    /// <summary>读出临时 Cookie 里的挑战引用；没有时返回 null。票据已过期或被篡改时同时签出。</summary>
    public async Task<string?> ReadAsync(HttpContext httpContext)
    {
        var result = await httpContext.AuthenticateAsync(Scheme);
        if (result.Succeeded)
        {
            return result.Principal.FindFirst(ChallengeClaimType)?.Value;
        }

        if (!result.None)
        {
            await httpContext.SignOutAsync(Scheme);
        }

        return null;
    }

    /// <summary>读出有效的待完成登录（在挑战自己的租户里核对）；带了临时 Cookie 却失效时签出并返回 null。</summary>
    public async Task<TwoFactorPrompt?> FindAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (await ReadAsync(httpContext) is not { } token)
        {
            return null;
        }

        var prompt = await authService.FindTwoFactorLoginAsync(token, cancellationToken);
        if (prompt is null)
        {
            await httpContext.SignOutAsync(Scheme);
        }

        return prompt;
    }

    /// <summary>结束当前请求带来的待完成登录：作废挑战并删临时 Cookie。请求没带临时 Cookie 时什么也不写。幂等。</summary>
    public async Task EndAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        var result = await httpContext.AuthenticateAsync(Scheme);
        if (result.None)
        {
            return;
        }

        if (result.Succeeded && result.Principal.FindFirst(ChallengeClaimType)?.Value is { } token)
        {
            await authService.CancelTwoFactorLoginAsync(token, cancellationToken);
        }

        await httpContext.SignOutAsync(Scheme);
    }

    /// <summary>以临时 Cookie 里的挑战引用执行第二步操作；请求带了临时 Cookie 而挑战已到期（或票据已失效）时删掉它，再把错误交给异常处理。</summary>
    /// <remarks>
    /// 异常处理中间件写错误响应前会清空响应头，此时写下的删除 Cookie 会一起丢掉；
    /// 签出登记在 <c>OnStarting</c> 里，错误响应开始发送时才写入，不受清空影响。
    /// </remarks>
    public async Task<T> RunAsync<T>(HttpContext httpContext, Func<string?, Task<T>> action)
    {
        var result = await httpContext.AuthenticateAsync(Scheme);
        var token = result.Succeeded ? result.Principal.FindFirst(ChallengeClaimType)?.Value : null;
        try
        {
            return await action(token);
        }
        catch (BusinessException exception) when (
            exception.Code == AuthErrorCodes.TwoFactorChallengeExpired && !result.None)
        {
            httpContext.Response.OnStarting(() => httpContext.SignOutAsync(Scheme));
            throw;
        }
    }
}
#endif
