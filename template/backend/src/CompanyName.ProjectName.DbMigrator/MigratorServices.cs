#if (RemoteTokenAuth)
using CompanyName.ProjectName.Application;
using CompanyName.ProjectName.Domain;
#endif
using CompanyName.ProjectName.Infrastructure;
#if (RemoteTokenAuth)
using Leistd.Security;
using Leistd.Tracing;
#if (!IncludeOperationRecords)
using Leistd.OperationRecords.Logging;
#endif
#endif
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
#if (RemoteTokenAuth && !IncludeOperationRecords)
using Microsoft.Extensions.Logging;
#endif

namespace CompanyName.ProjectName.DbMigrator;

/// <summary>
/// 迁移作业的服务组合。宿主与注册面测试共用这一份，测试校验的就是作业实际解析的组合。
/// </summary>
public static class MigratorServices
{
    public static IServiceCollection AddMigratorServices(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        // 只注册持久化：迁移目标枚举（ITenantMigrationTargetProvider）随租户连接解析一起注册。
        // 运行期组件依赖只在 API 里注册的当前用户与权限主体，这里用不上
        services.AddPersistenceServices(configuration);
#if (LocalIdentity && IncludeMultiTenancy)
        // 独立库连接串在控制库里加密存储：必须与 API 共享同一密钥环，否则解不开、迁移作业整体停下
        services.AddMyProjectDataProtection(configuration, environment);
#endif
        services.AddScoped<DatabaseMigrationRunner>();
        return services;
    }
#if (RemoteTokenAuth)

    /// <summary>
    /// 首位管理员引导命令的组合：比迁移多组合应用能力（初始化、授权、操作记录），因为引导要走正式业务路径。
    /// </summary>
    public static IServiceCollection AddResourceAdminBootstrapServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAmbientContext();
        services.AddCorrelationIdCore();
        services.AddDomainServices();
        services.AddInfrastructureServices(configuration);
        services.AddApplicationServices();
#if (!IncludeOperationRecords)
        services.AddOperationRecordsLogging();
        services.AddLogging(logging => logging.AddFilter("Leistd.OperationRecords", LogLevel.Information));
#endif
        services.AddScoped<ResourceAdminBootstrapRunner>();
        return services;
    }
#endif
}
