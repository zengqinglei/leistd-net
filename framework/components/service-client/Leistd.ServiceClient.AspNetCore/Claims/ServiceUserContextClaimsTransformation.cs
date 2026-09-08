using System.Security.Claims;
using Leistd.ServiceClient.AspNetCore.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Leistd.ServiceClient.AspNetCore.Claims;

/// <summary>
/// 在认证阶段恢复服务用户上下文的 <see cref="IClaimsTransformation"/>。
/// </summary>
/// <remarks>
/// 恢复必须在 <see cref="IClaimsTransformation"/> 里而不能只靠中间件改写 <c>HttpContext.User</c>：
/// 授权策略显式声明认证 scheme 时，<c>PolicyEvaluator</c> 会重新认证并覆盖中间件写入的主体。
/// 转换幂等：已恢复过的主体原样返回。
/// 信任判定见 <c>ServiceUserContext.IsTrustedServiceCall</c>——调用方须是 <see cref="Leistd.Security.Claims.ClientSubject"/> 契约的机器主体。
/// </remarks>
/// <param name="httpContextAccessor">HTTP 上下文访问器（读取请求头）</param>
/// <param name="optionsMonitor">恢复配置监视器</param>
/// <param name="logger">日志</param>
public sealed class ServiceUserContextClaimsTransformation(
    IHttpContextAccessor httpContextAccessor,
    IOptionsMonitor<ServiceUserContextOptions> optionsMonitor,
    ILogger<ServiceUserContextClaimsTransformation> logger) : IClaimsTransformation
{
    /// <inheritdoc />
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        var opts = optionsMonitor.CurrentValue;
        if (!opts.Enabled ||
            ServiceUserContext.IsEnriched(principal, opts) ||
            !ServiceUserContext.IsTrustedServiceCall(principal, opts))
        {
            return Task.FromResult(principal);
        }

        var headers = httpContextAccessor.HttpContext?.Request.Headers;
        if (headers is null)
        {
            return Task.FromResult(principal);
        }

        var restored = ServiceUserContext.TryRestore(principal, headers, opts);
        if (restored is null)
        {
            return Task.FromResult(principal);
        }

        logger.LogDebug(
            "Restored user context from service invocation headers: {UserId}",
            restored.FindFirst(ServiceUserContext.SubjectClaimType)?.Value);
        return Task.FromResult(restored);
    }
}
