#if (LocalIdentity)
using Leistd.Data;
using Leistd.ExceptionHandling;
using Leistd.MultiTenancy;
using Leistd.MultiTenancy.ConnectionStrings;
using CompanyName.ProjectName.Infrastructure.Persistence;
using Leistd.MultiTenancy.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Leistd.Data.Abstractions;
using Leistd.MultiTenancy.Abstractions;

namespace CompanyName.ProjectName.Infrastructure.TenantConnections;

/// <summary>
/// Identity 宿主的连接解析：直接读本服务的控制库。
/// </summary>
/// <remarks>
/// <para><b>不变量：必须直接注入 <see cref="IdentityControlDbContext"/>，不能经
/// <c>IDbContextProvider</c> 获取。</b>Provider 在创建任何 DbContext 之前都会先调用本解析器，
/// 若本解析器反过来经 Provider 拿上下文就形成递归，表现为栈溢出。控制库固定在宿主连接上、
/// 不参与租户路由，所以它本来也不需要 Provider 那层。</para>
/// <para>回落链见 <see cref="IConnectionStringResolver.ResolveAsync"/> 的约定，此处逐级对应。</para>
/// </remarks>
internal sealed class LocalTenantConnectionStringResolver(
    ICurrentTenant currentTenant,
    IdentityControlDbContext controlDbContext,
    ISecretResolver secretResolver,
    IConfiguration configuration) : IConnectionStringResolver
{
    public async Task<string> ResolveAsync(
        string connectionStringName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionStringName);

        // 控制面始终使用宿主连接，避免租户路由的循环依赖。
        if (string.Equals(
                connectionStringName,
                IdentityControlDbContext.ConnectionStringName,
                StringComparison.Ordinal))
        {
            return configuration.GetControlPlaneConnectionString()!;
        }

        var defaultConnection = configuration.GetConnectionString(connectionStringName);

        if (!currentTenant.IsAvailable)
        {
            return defaultConnection!;
        }

        var tenantId = currentTenant.Id!.Value;

        // 控制面没有软删除过滤器，必须显式排除已删除租户的连接配置。
        var record = await controlDbContext.ConnectionsOfUndeletedTenants()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken)
            // 缺少租户配置不可通过重试恢复，因此返回 404。
            ?? throw new NotFoundException(
                $"Tenant '{tenantId}' has no connection configuration.");

        return record.DatabaseMode switch
        {
            TenantDatabaseMode.SharedDatabase => defaultConnection!,

            TenantDatabaseMode.DedicatedDatabase when !string.IsNullOrWhiteSpace(record.RuntimeSecretReference) =>
                await secretResolver.ResolveAsync(record.RuntimeSecretReference, cancellationToken),

            // 独立库缺少 Secret 时失败关闭，不能回退到共享库。
            _ => throw new InternalServerException(
                $"Tenant '{tenantId}' connection configuration is corrupt: " +
                $"mode is {record.DatabaseMode} but the runtime Secret reference is missing.")
        };
    }
}
#endif
