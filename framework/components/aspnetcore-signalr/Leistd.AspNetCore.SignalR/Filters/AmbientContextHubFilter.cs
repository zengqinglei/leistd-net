using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Leistd.AmbientContext;
using Leistd.AspNetCore.SignalR.Options;

namespace Leistd.AspNetCore.SignalR.Filters;

/// <summary>
/// 为每次 Hub 调用建立环境上下文，并复评宿主授权策略。
/// </summary>
/// <remarks>
/// <para><b>它补的是 SignalR 的结构性落差</b>：握手是一次 HTTP 请求，会走完整中间件管道，
/// 因此主体、租户、链路标识与端点策略都成立；而 WebSocket 升级之后的<b>方法调用不经中间件</b>，
/// 上述四项全部不成立。症状是订阅授权拿不到判断材料、账号禁用对已建连接无效、
/// Hub 里落库把多租户实体写成宿主行——三者同一个根。</para>
/// <para>连接的 <c>ClaimsPrincipal</c> 是握手时认证出来的，作为身份来源可信；
/// 但"这个身份现在还有效吗"必须复评，见 <see cref="HubIdentityOptions.PolicyName"/>。</para>
/// <para>本过滤器是<b>全局</b>的，因此不属于任何单个 Hub 组件——realtime 与 notifications
/// 都需要它，谁都不该拥有它。</para>
/// </remarks>
public sealed class AmbientContextHubFilter(
    IOptions<HubIdentityOptions> options,
    ILogger<AmbientContextHubFilter> logger,
    TimeProvider? timeProvider = null) : IHubFilter
{
    // 时间源可注入：复评节流窗口靠它计时，写死 DateTimeOffset.UtcNow 的话
    // 「RevalidationInterval 到底有没有生效」只能靠真等一段时间来验证。
    // 默认落 TimeProvider.System，宿主无需为此多配一项。
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    // 上下文一律建立，复评只对有身份的连接做——两件事分开：
    // 匿名 Hub 仍需要链路标识和宿主自定义贡献者，但没有身份可复评。
    // 判匿名要看 IsAuthenticated 而不是 principal is null：ASP.NET Core 给未认证请求的是
    // 一个非 null 的空 ClaimsPrincipal，按 null 判会让 [AllowAnonymous] 的 Hub 撞上
    // 默认策略的 RequireAuthenticatedUser 被 Abort()。
    private static ClaimsPrincipal Principal(HubCallerContext context) => context.User ?? new ClaimsPrincipal();

    private static bool IsAuthenticated(ClaimsPrincipal principal) =>
        principal.Identity?.IsAuthenticated == true;

    private const string LastRevalidatedKey = "Leistd.AspNetCore.SignalR.LastRevalidated";

    /// <inheritdoc />
    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var principal = Principal(invocationContext.Context);

        // 复评必须在上下文之内：宿主的授权 handler 通常读环境态（如 ICurrentUser.Id）
        // 而不是 AuthorizationHandlerContext.User——那是为 HTTP 路径写的正常写法。
        // 先复评会让它读到空主体，把每个人都判成失效并中止连接。
        using (Begin(invocationContext.ServiceProvider, principal))
        {
            if (IsAuthenticated(principal))
            {
                await RevalidateOrAbortAsync(
                    invocationContext.ServiceProvider,
                    invocationContext.Context,
                    principal,
                    invocationContext.HubMethodName);
            }

            return await next(invocationContext);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// 连接建立也要包在上下文里：<c>OnConnectedAsync</c> 里常做加组这类动作，
    /// 那些同样需要租户与主体。
    /// </remarks>
    public async Task OnConnectedAsync(
        HubLifetimeContext context,
        Func<HubLifetimeContext, Task> next)
    {
        using (Begin(context.ServiceProvider, Principal(context.Context)))
        {
            await next(context);
        }
    }

    /// <inheritdoc />
    public Task OnDisconnectedAsync(
        HubLifetimeContext context,
        Exception? exception,
        Func<HubLifetimeContext, Exception?, Task> next)
    {
        // 断开清理同样需要上下文，否则清理会落到宿主分区。
        return DisconnectAsync(context, exception, next, Principal(context.Context));
    }

    private async Task DisconnectAsync(
        HubLifetimeContext context,
        Exception? exception,
        Func<HubLifetimeContext, Exception?, Task> next,
        System.Security.Claims.ClaimsPrincipal principal)
    {
        using (Begin(context.ServiceProvider, principal))
        {
            await next(context, exception);
        }
    }

    private static IDisposable Begin(
        IServiceProvider serviceProvider,
        System.Security.Claims.ClaimsPrincipal principal)
        => serviceProvider.GetRequiredService<IAmbientContext>().Begin(principal);

    // 账号有效性复评。不通过即中止连接：让客户端重连并重新认证，
    // 而不是让它继续持有一个已经失效的身份反复调用。
    private async Task RevalidateOrAbortAsync(
        IServiceProvider serviceProvider,
        HubCallerContext callerContext,
        System.Security.Claims.ClaimsPrincipal principal,
        string hubMethodName)
    {
        var interval = options.Value.RevalidationInterval;
        if (interval.HasValue &&
            callerContext.Items.TryGetValue(LastRevalidatedKey, out var last) &&
            last is DateTimeOffset lastAt &&
            _timeProvider.GetUtcNow() - lastAt < interval.Value)
        {
            return;
        }

        var authorization = serviceProvider.GetService<IAuthorizationService>();
        if (authorization is null)
        {
            // 宿主没装授权：没有策略可复评，跳过。缺失是宿主的选择，不是本过滤器的默认。
            return;
        }

        var result = options.Value.PolicyName is { } policyName
            ? await authorization.AuthorizeAsync(principal, resource: null, policyName)
            : await AuthorizeWithDefaultPolicyAsync(serviceProvider, authorization, principal);

        callerContext.Items[LastRevalidatedKey] = _timeProvider.GetUtcNow();

        if (result.Succeeded)
        {
            return;
        }

        logger.LogInformation(
            "Aborting hub connection {ConnectionId}: the principal no longer satisfies the policy (method {HubMethod})",
            callerContext.ConnectionId, hubMethodName);

        callerContext.Abort();
        throw new HubException("The connection is no longer authorized.");
    }

    private static async Task<AuthorizationResult> AuthorizeWithDefaultPolicyAsync(
        IServiceProvider serviceProvider,
        IAuthorizationService authorization,
        System.Security.Claims.ClaimsPrincipal principal)
    {
        var policyProvider = serviceProvider.GetService<IAuthorizationPolicyProvider>();
        if (policyProvider is null)
        {
            return AuthorizationResult.Success();
        }

        var policy = await policyProvider.GetDefaultPolicyAsync();
        return await authorization.AuthorizeAsync(principal, resource: null, policy.Requirements);
    }
}
