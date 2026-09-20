using System.Net;
using Leistd.MultiTenancy.Abstractions;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.MultiTenancy.Dtos;
using Leistd.MultiTenancy.ServiceClient.Options;
using Leistd.ServiceClient.Http;
using Microsoft.Extensions.Options;

namespace Leistd.MultiTenancy.ServiceClient.Stores;

// 回源控制面的机器端点，响应形态与端点共用 Core 里的线上 DTO。
// 只有"租户不存在"（404 且带 Tenant:NotFound）翻成 null：路由不对等其它 404 原样抛出，
// 否则前缀配错会表现成"所有租户都不存在"。其余错误同样上抛，交给弹性策略与全局异常处理——
// 吞成 null 会让"控制面不可达"表现成"这个租户不存在"。
internal sealed class RemoteTenantConnectionStore(
    HttpClient httpClient,
    IOptions<RemoteTenantConnectionClientOptions> options) : ITenantConnectionConfigurationStore
{
    public async Task<TenantConnectionLookupResult?> FindAsync(
        Guid tenantId,
        string name,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync(
            $"{Prefix}/runtime/{tenantId}?name={Uri.EscapeDataString(name)}",
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var error = await response.CreateRemoteErrorAsync(cancellationToken);
            if (error.ErrorCode == MultiTenancyErrorCodes.NotFound)
            {
                return null;
            }

            throw error;
        }

        var remote = await response.ReadContentAsync<TenantRuntimeConnectionOutputDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("The control plane returned an empty tenant connection lookup.");

        return new TenantConnectionLookupResult
        {
            TenantId = remote.TenantId,
            HasAnyConnection = remote.HasAnyConnection,
            Connection = remote.Connection is { } connection
                ? new TenantConnectionConfiguration
                {
                    TenantId = remote.TenantId,
                    Name = connection.Name,
                    ConnectionString = connection.ConnectionString,
                    Version = connection.Version
                }
                : null
        };
    }

    public async Task<IReadOnlyList<TenantMigrationConnection>> GetListAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"{Prefix}/migration?name={Uri.EscapeDataString(name)}", cancellationToken);
        var remote = await response.ReadContentAsync<List<TenantMigrationConnectionOutputDto>>(cancellationToken: cancellationToken) ?? [];
        return [.. remote.Select(x => new TenantMigrationConnection(x.TenantId, x.Name, x.ConnectionString))];
    }

    private string Prefix => "/" + options.Value.RoutePrefix.Trim('/');
}
