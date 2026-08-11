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
/// 恢复必须发生在这里而不能只靠中间件改写 <c>HttpContext.User</c>：
/// 授权策略显式声明认证 scheme 时（如模板的默认策略），<c>PolicyEvaluator</c> 会按 scheme
/// **重新认证**并用其结果覆盖 <c>HttpContext.User</c>——中间件改写的主体在该路径上会被丢弃。
/// <see cref="IClaimsTransformation"/> 在每一次 <c>AuthenticateAsync</c> 内部生效（含策略重认证），
/// 是唯一对两条路径都成立的挂载点。转换幂等：已恢复过的主体原样返回。
/// </remarks>
/// <param name="httpContextAccessor">HTTP 上下文访问器（读取请求头）</param>
/// <param name="options">恢复配置</param>
/// <param name="logger">日志</param>
public sealed class ServiceUserContextClaimsTransformation(
    IHttpContextAccessor httpContextAccessor,
    IOptions<ServiceUserContextOptions> options,
    ILogger<ServiceUserContextClaimsTransformation> logger) : IClaimsTransformation
{
    /// <inheritdoc />
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        var opts = options.Value;
        if (!opts.Enable ||
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
            "已从服务调用头恢复用户上下文: {UserId}",
            restored.FindFirst(ServiceUserContext.SubjectClaimType)?.Value);
        return Task.FromResult(restored);
    }
}
