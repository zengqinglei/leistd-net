using Microsoft.Extensions.Options;
using Leistd.MultiTenancy.Abstractions;
using Leistd.MultiTenancy.AspNetCore.Options;
using Leistd.MultiTenancy.AspNetCore.Resolution;
using Leistd.AmbientContext;

namespace Leistd.MultiTenancy.AspNetCore.AmbientContext;

// 非 HTTP 入口的租户维度：从主体的租户声明建立 ICurrentTenant。
//
// 非 HTTP 入口只从已验证主体的 claim 建立租户，不读取请求参数。
//
// 不做租户存在性与启用状态校验：那是请求入口的职责（MultiTenancyMiddleware），
// 连接建立之后的失效判定统一走宿主授权策略。
internal sealed class TenantAmbientContextContributor(
    ICurrentTenant currentTenant,
    IOptions<MultiTenancyOptions> options) : IAmbientContextContributor
{
    /// <inheritdoc />
    public IDisposable? Enter(AmbientContextEnterContext context)
    {
        if (context.Principal.Identity?.IsAuthenticated != true)
        {
            // 未认证：没有可信的租户来源，不猜，保持进入前的状态。
            return null;
        }

        var claimValue = TenantClaimReader.Read(context.Principal, options.Value.TenantClaimType);

        if (claimValue is null)
        {
            // 无声明即宿主。仍要显式 Change(null)：入口可能继承了外层的租户上下文，
            // 不置位会让宿主主体读到别人的租户分区。
            return currentTenant.Change(null);
        }

        // 非法租户 claim 属于契约错误，不能回退到宿主分区。
        if (!Guid.TryParse(claimValue, out var tenantId))
        {
            throw new InvalidOperationException(
                $"The tenant claim '{options.Value.TenantClaimType}' is not a GUID. " +
                "The framework treats the tenant claim as the authoritative tenant id for " +
                "non-HTTP entry points; falling back to the host view would silently place a " +
                "tenant user in the host partition.");
        }

        return currentTenant.Change(tenantId);
    }
}
