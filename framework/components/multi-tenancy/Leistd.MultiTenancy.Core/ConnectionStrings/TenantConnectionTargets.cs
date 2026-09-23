using Leistd.Data.Connections;
using Leistd.ExceptionHandling;
using Microsoft.Extensions.Configuration;

namespace Leistd.MultiTenancy.ConnectionStrings;

// 本地与远端两种解析共用的判定：宿主连接的取法，以及"查到什么就用什么"的三级落点。
internal static class TenantConnectionTargets
{
    // 宿主自己的连接：先按名字找，再回落默认名。
    // 回落是有意的：业务项目把上下文改名为 Crm 之后，部署里仍然只配 ConnectionStrings__Default，不用改部署。
    public static string HostConnection(string connectionStringName, IConfiguration configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionStringName);

        return Configured(configuration.GetConnectionString(connectionStringName))
               ?? Configured(configuration.GetConnectionString(ConnectionStringNames.Default))
               ?? throw new InvalidOperationException(
                   $"Neither ConnectionStrings:{connectionStringName} nor " +
                   $"ConnectionStrings:{ConnectionStringNames.Default} is configured.");
    }

    // 消息只带租户与连接名，绝不带连接串
    public static string Select(
        Guid tenantId,
        string connectionStringName,
        TenantConnectionLookupResult lookup,
        IConfiguration configuration)
    {
        // 一条连接都没登记：该租户不单独分库，用这个服务自己配置的库
        if (!lookup.HasAnyConnection)
        {
            return HostConnection(connectionStringName, configuration);
        }

        // 精确名或默认名命中（回落已由存储实现完成）
        if (lookup.Connection is { } connection)
        {
            return connection.ConnectionString;
        }

        // 租户登记过连接（说明它是分库租户），却偏偏缺了这个服务的、也没有默认名可回落。
        // 这时静默连到公共库是事故：那个库里没有它的数据，而它的写入会落进别人的库。
        throw new InvalidOperationException(
            $"Tenant '{tenantId}' has tenant-specific connections registered but none for " +
            $"'{connectionStringName}', and no default-named connection to fall back to. " +
            "Refusing to fall back to this service's own database.");
    }

    private static string? Configured(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
