#if (!LocalIdentity)
using System.Net;
using Leistd.MultiTenancy.ConnectionStrings;
using Refit;

namespace CompanyName.ProjectName.Infrastructure.TenantConnections;

/// <summary>
/// 把 Identity 的租户连接端点适配成框架的连接存储。
/// </summary>
/// <remarks>
/// <para>框架用同一个契约表达两种形态：持有控制库的服务注册 EF 实现，本服务注册这个 HTTP 实现。
/// 解析、缓存、单飞与跨租户校验都在框架里，这里只负责调用 Identity 并转换响应形态。
/// 换用 Identity 团队发布的正式 Client 包时，只替换本类。</para>
/// <para>404 翻成 <see langword="null"/>：契约规定"租户不存在或已删除返回 null"，由解析器统一失败关闭，
/// 与 EF 实现的语义一致。其余错误原样上抛，交给弹性策略与全局异常处理——把它们也吞成
/// <see langword="null"/> 会让"Identity 不可达"表现成"这个租户不存在"。</para>
/// </remarks>
internal sealed class IdentityTenantConnectionStore(IIdentityTenantConnectionClient client)
    : ITenantConnectionConfigurationStore
{
    public async Task<TenantConnectionLookupResult?> FindAsync(
        Guid tenantId,
        string name,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var remote = await client.GetRuntimeAsync(tenantId, name, cancellationToken);
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
        catch (ApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<TenantMigrationConnection>> GetListAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        var remote = await client.GetMigrationListAsync(name, cancellationToken);
        return [.. remote.Select(x => new TenantMigrationConnection(x.TenantId, x.Name, x.ConnectionString))];
    }
}
#endif
