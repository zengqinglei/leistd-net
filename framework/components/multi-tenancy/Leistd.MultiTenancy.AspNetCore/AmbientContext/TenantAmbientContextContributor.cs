using Microsoft.Extensions.Options;
using Leistd.MultiTenancy.Abstractions;
using Leistd.MultiTenancy.AspNetCore.Options;
using Leistd.MultiTenancy.AspNetCore.Resolution;
using Leistd.AmbientContext;

namespace Leistd.MultiTenancy.AspNetCore.AmbientContext;

// 非 HTTP 入口的租户维度：从主体的租户声明建立 ICurrentTenant。
//
// 只认 claim，不跑完整解析链：Hub 调用与后台作业没有请求头与查询串，
// 而解析链的其它贡献者会去读 IHttpContextAccessor（那里为 null）或采信不可信来源。
// 这也与既有政策一致——已认证用户的租户由 claim 定案，请求参数无法改写。
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

        // 有声明但不是 Guid：这是签发端与消费端的口径不一致，属于配置错误。
        // 绝不能退回宿主视角——那会把一个租户用户静默放进宿主分区，
        // 既读得到别人的数据，写入也落错归属，且没有任何信号。
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
