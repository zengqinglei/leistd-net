using Leistd.MultiTenancy;
using Leistd.ServiceClient.Constants;
using Leistd.MultiTenancy.Abstractions;
using Leistd.ServiceClient.Options;
using Microsoft.Extensions.Options;

namespace Leistd.ServiceClient.Handlers;

/// <summary>
/// 将当前租户标识写入出站请求头。
/// </summary>
/// <remarks>
/// 读取 <see cref="ICurrentTenant"/>（环境上下文）而非用户 claim——
/// 后台任务 <c>Change(tenantId)</c> 后无主体也能传递；用户 token 场景租户已随
/// <c>tenant_id</c> claim 在 token 内传递，本头服务于 client credentials 通道。
/// 请求已有同名头时不覆盖。
/// </remarks>
public class TenantContextDelegatingHandler<TOptions>(
    ICurrentTenant currentTenant,
    IOptionsMonitor<TOptions> optionsMonitor) : DelegatingHandler
    where TOptions : ServiceClientOptions
{
    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var options = optionsMonitor.CurrentValue.UserContext;
        if (options.ForwardTenantId &&
            currentTenant.Id is { } tenantId &&
            !request.Headers.Contains(ServiceClientHeaders.TenantId))
        {
            request.Headers.TryAddWithoutValidation(ServiceClientHeaders.TenantId, tenantId.ToString());
        }

        return base.SendAsync(request, cancellationToken);
    }
}
