using System.Security.Claims;

namespace Leistd.AmbientContext;

/// <summary>
/// 为非 HTTP 入口一次性建立环境上下文（主体，以及已注册的其它维度）。
/// </summary>
/// <remarks>
/// <para>用于 SignalR Hub 方法调用、后台作业、消息消费者这类<b>拿到完整身份才进入</b>的入口。
/// 这些入口不经 ASP.NET Core 中间件，因此中间件建立的主体、租户与链路标识在其中都不成立。</para>
/// <para>HTTP 请求不使用本接口，理由见 <see cref="IAmbientContextContributor"/>。</para>
/// </remarks>
/// <example>
/// <code>
/// using (ambientContext.Begin(hubCallerContext.User!))
/// {
///     // 此作用域内 ICurrentUser、ICurrentTenant、ICorrelationIdProvider 均成立
/// }
/// </code>
/// </example>
public interface IAmbientContext
{
    /// <summary>
    /// 建立环境上下文；释放返回值即还原到进入前的状态。
    /// </summary>
    /// <param name="principal">本次调用的主体。</param>
    /// <param name="correlationId">指定链路标识；省略时由追踪维度决定。</param>
    IDisposable Begin(ClaimsPrincipal principal, string? correlationId = null);
}
