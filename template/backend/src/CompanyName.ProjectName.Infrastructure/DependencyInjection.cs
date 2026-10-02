using Leistd.Auditing.EntityFrameworkCore;
using Leistd.Authorization.EntityFrameworkCore;
using Leistd.OperationRecords.EntityFrameworkCore;
using Leistd.Settings.EntityFrameworkCore;
#if (LocalIdentity)
using Leistd.MultiTenancy.EntityFrameworkCore;
using Leistd.MultiTenancy.Management.Provisioning;
#endif
using Leistd.BackgroundJobs.EntityFrameworkCore;
using Leistd.Ddd.Infrastructure;
using Leistd.Ddd.Infrastructure.EventBus;
using Leistd.Lock.Redis;
using Leistd.Lock.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Leistd.UnitOfWork.EntityFrameworkCore.Database;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using CompanyName.ProjectName.Infrastructure.Persistence;
using Leistd.MultiTenancy;
#if (!LocalIdentity)
using Leistd.MultiTenancy.ServiceClient;
using Leistd.ServiceClient.OAuth;
using Leistd.ServiceClient.OAuth.Options;
#endif
#if (LocalIdentity)
using CompanyName.ProjectName.Infrastructure.TenantConnections;
#endif
#if (IncludeNotifications)
using Leistd.Notifications.EntityFrameworkCore;
#endif

using CompanyName.ProjectName.Domain.Shared.Security.PasswordHash;
#if (LocalIdentity)
using CompanyName.ProjectName.Infrastructure.Shared.Security.VerificationCodes;
using CompanyName.ProjectName.Domain.Auth.VerificationCodes;
#endif
using CompanyName.ProjectName.Infrastructure.Shared.Security.PasswordHash;

#if (LocalIdentity)
using Leistd.Email.Smtp;
#endif
#if (ExternalLogin)
using CompanyName.ProjectName.Domain.Auth.Options;
using CompanyName.ProjectName.Infrastructure.Auth.OAuth.Options;
using Microsoft.Extensions.Options;
#endif
using StackExchange.Redis;
using Leistd.Auditing.EntityFrameworkCore.Interceptors;
using Leistd.Data;
using Leistd.Data.Connections;

namespace CompanyName.ProjectName.Infrastructure;

/// <summary>
/// 提供基础设施层服务注册。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册基础设施层服务：持久化（见 <see cref="AddPersistenceServices"/>）加上运行期组件、缓存与锁、外部适配器。
    /// </summary>
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddPersistenceServices(configuration);

#if (LocalIdentity)
        // 开通失败的数据库错误翻译：SQLSTATE 表是 PostgreSQL 方言，属本项目的技术适配。
        // 组件按 TryAdd 挂"不翻译"的默认实现，这里直接登记，与注册先后无关
        services.AddSingleton<ITenantDatabaseErrorDescriber, PostgresTenantDatabaseErrorDescriber>();
#endif

#if (IncludeNotifications)
        services.AddNotificationsEfCore<MyProjectDbContext>();
        // 旧通知的保留期清理（默认开启：已读 90 天、未读 365 天，配置节 Leistd:Notifications:Retention）
        services.AddNotificationRetention<MyProjectDbContext>();
#endif
        services.AddPermissionAuthorizationEfCore<MyProjectDbContext>();
        services.AddSettingsEfCore<MyProjectDbContext>();
        services.AddOperationRecordsEfCore<MyProjectDbContext>();
        // 到期记录搬入归档表。默认关闭：审计表只增不减是安全的默认值，
        // 要启用就得有人显式打开（配置 Leistd:OperationRecords:Retention 或系统设置的「审计」面板）
        services.AddOperationRecordRetention<MyProjectDbContext>();
        // 集群周期任务的完成水位与业务表同库：多副本同一时段只跑一次
        services.AddBackgroundJobsEfCore<MyProjectDbContext>();
        // 连接串使用 StackExchange.Redis 原生格式（host:port,password=...,ssl=true），原样交给官方解析器。
        var redisConnStr = configuration.GetConnectionString("Redis");

        if (!string.IsNullOrEmpty(redisConnStr))
        {
            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnStr));

            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnStr;
                options.InstanceName = "MyProject:";
            });

            services.AddRedisDistributedLock(redisConnStr);
        }
        else
        {
            services.AddDistributedMemoryCache();
            services.AddMemoryLocalLock();
        }

        services.AddOptions<PasswordHashOptions>()
            .Bind(configuration.GetSection(PasswordHashOptions.SectionName))
            .Validate(options => options.IterationCount > 0, $"{PasswordHashOptions.SectionName}:IterationCount must be greater than 0.")
            .ValidateOnStart();
        services.AddTransient<IPasswordHasher, PasswordHasher>();
#if (LocalIdentity)
        // 验证码摘要与口令哈希具有不同的密钥和成本契约。
        services.AddSingleton<IVerificationCodeDigest, HmacVerificationCodeDigest>();
#endif

#if (LocalIdentity)
        services.AddSmtpEmailSender();
#endif

#if (ExternalLogin)
        services.AddSingleton<IValidateOptions<ExternalAuthOptions>, ExternalAuthOptionsValidator>();
        services.AddOptions<ExternalAuthOptions>()
            .Configure<IConfiguration>((options, config) =>
            {
                var section = config.GetSection(ExternalAuthOptions.SectionName);
                BindProvider(options.Github, section.GetSection("Github"));
                BindProvider(options.Google, section.GetSection("Google"));
            })
            .ValidateOnStart();
        services.AddHttpClient();
#endif

        return services;
    }

    /// <summary>
    /// 注册持久化：数据库上下文、租户连接解析与多租户控制库。
    /// </summary>
    /// <remarks>
    /// 迁移作业（DbMigrator）只用这一部分：运行期组件（设置、权限、操作记录等）依赖只在 API 里注册的当前用户与权限主体，
    /// 迁移进程注册它们既用不上、也会让开发环境的容器构建期校验失败。
    /// </remarks>
    public static IServiceCollection AddPersistenceServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 缺连接串在首次创建上下文时失败并指明键名。API 在接流量之前校验迁移（会创建全部上下文），
        // 因此这个错误发生在启动期，不会变成每个请求一次的 500。
        string RequireConnection(string? connectionString) =>
            string.IsNullOrWhiteSpace(connectionString)
                ? throw new InvalidOperationException(
                    $"No database is configured. Set ConnectionStrings:{ConnectionStringNames.Default}; " +
                    "for local development start deploy/docker-compose.dev.yml (appsettings.Development.json points at it).")
                : connectionString;

        services.AddMemoryCache();

#if (!LocalIdentity)
        // 远端解析：向 Identity 回源租户连接配置，按 TenantRouting:CacheLifetime 缓存（默认 10 分钟，
        // 它决定租户改路由前的排空等待，可按环境覆盖）；同租户并发回源合并为一次。
        // 远端存储由框架提供，回源 Identity 经 MapTenantConnections 暴露的机器端点（配置节 Leistd:ServiceClients:Identity）。
        services.AddRemoteTenantConnectionResolution();
        var identityClient = services.AddRemoteTenantConnectionStore("Identity", configuration);
        if (configuration.GetSection(ServiceAuthenticationOptions.SectionName).Exists())
        {
            services.AddServiceAuthentication();
            identityClient.AddClientCredentials();
        }
        identityClient.AddStandardResilienceHandler();
#else
        // 本地解析：直接读本服务的控制库；控制库固定在宿主连接上，不参与租户路由。
        services.AddLocalTenantConnectionResolution<IdentityControlDbContext>(
            options => options.ControlPlaneConnectionStringName = IdentityControlDbContext.ConnectionStringName);
#endif

#if (LocalIdentity)
        services.AddDbContext<IdentityControlDbContext>((sp, options) =>
        {
            // 控制面不继承 BaseDbContext，必须显式挂载修改和删除审计拦截器。
            options.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());

            options.UseNpgsql(RequireConnection(configuration.GetControlPlaneConnectionString()), npgsql =>
                npgsql.MigrationsHistoryTable(
                    DatabaseSchema.ControlMigrationsHistoryTable, DatabaseSchema.Name));
        });
#endif

#if (OpenIddictServer)
        // OIDC 与控制面同库同 schema，但使用独立迁移历史。
        // 动态注入的 OIDC 实体不在快照中，因此只在此处抑制模型差异警告。
        services.AddDbContext<OpenIddictDbContext>(options =>
        {
            options.UseNpgsql(RequireConnection(configuration.GetControlPlaneConnectionString()), npgsql =>
                npgsql.MigrationsHistoryTable(
                    OpenIddictDbContext.MigrationsHistoryTable, DatabaseSchema.Name));

            options.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
            options.UseOpenIddict();
        });
#endif

        services.AddDbContext<MyProjectDbContext>(options =>
        {
            var creationContext = DbContextCreationContext.Current;
            void ConfigureNpgsql(NpgsqlDbContextOptionsBuilder npgsql)
            {
                npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                npgsql.MigrationsHistoryTable(
                    DatabaseSchema.BusinessMigrationsHistoryTable, DatabaseSchema.Name);
            }

            if (creationContext?.ExistingConnection is NpgsqlConnection existingConnection)
            {
                options.UseNpgsql(existingConnection, ConfigureNpgsql);
            }
            else
            {
                options.UseNpgsql(
                    RequireConnection(creationContext?.ConnectionString
                        ?? configuration.GetConnectionString(ConnectionStringNames.Default)),
                    ConfigureNpgsql);
            }

            // SplitQuery 已处理多集合查询；模型与迁移不一致仍必须失败。
            options.ConfigureWarnings(w => w
                .Ignore(RelationalEventId.MultipleCollectionIncludeWarning));
        });

#if (LocalIdentity)
        // 租户注册表与连接登记的存储；管理用例只在 API 里注册（AddApplicationServices）
        services.AddMultiTenancyEfCore<IdentityControlDbContext>();
#endif

        services.AddDddInfrastructure();

        // 每个注册过的 DbContext 都必须显式接入：漏掉的上下文会逃出租户过滤器闸门，
        // 构建容器时会直接失败。不传选项即"只登记、不注册仓储"。
        // 业务上下文继承 BaseDbContext，登记时同时挂上审计、领域事件与并发标记三个保存拦截器。
        services.AddDddDbContext<MyProjectDbContext>(options => options.AddDefaultRepositories());
#if (LocalIdentity)
        // 控制面上下文的租户注册表经自己的 Store 访问，不需要仓储。
        services.AddDddDbContext<IdentityControlDbContext>();
#endif
#if (OpenIddictServer)
        // OpenIddict 自有实体不是 Leistd 实体，只登记。
        services.AddDddDbContext<OpenIddictDbContext>();
#endif

        return services;
    }

#if (ExternalLogin)
    private static void BindProvider(
        ExternalAuthOptions.ProviderOptions options,
        IConfiguration configuration)
    {
        options.ClientId = configuration[nameof(options.ClientId)];
        options.ClientSecret = configuration[nameof(options.ClientSecret)];
    }
#endif
}
