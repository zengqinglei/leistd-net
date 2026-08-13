using Leistd.MultiTenancy;
using Leistd.ServiceClient.Constants;

namespace Leistd.ServiceClient.Handlers;

/// <summary>
/// 租户上下文出站处理器：当前存在租户上下文时注入 <c>X-Tenant-Id</c> 头
/// </summary>
/// <remarks>
/// 读取 <see cref="ICurrentTenant"/>（环境上下文）而非用户 claim——
/// 后台任务 <c>Change(tenantId)</c> 后无主体也能传递；用户 token 场景租户已随
/// <c>tenant_id</c> claim 在 token 内传递，本头服务于 client credentials 通道。
/// 请求已有同名头时不覆盖。
/// </remarks>
public class TenantContextDelegatingHandler(ICurrentTenant currentTenant) : DelegatingHandler
{
    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (currentTenant.Id is { } tenantId &&
            !request.Headers.Contains(ServiceClientHeaders.TenantId))
        {
            request.Headers.TryAddWithoutValidation(ServiceClientHeaders.TenantId, tenantId.ToString());
        }

        return base.SendAsync(request, cancellationToken);
    }
}
