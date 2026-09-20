using Leistd.Auditing.EntityFrameworkCore;
using Leistd.Authorization.EntityFrameworkCore;
using Leistd.OperationRecords.EntityFrameworkCore;
using Leistd.Settings.EntityFrameworkCore;
#if (LocalIdentity)
using Leistd.MultiTenancy.EntityFrameworkCore;
#endif
using Leistd.BackgroundJobs.EntityFrameworkCore;
using Leistd.Ddd.Infrastructure;
using Leistd.Ddd.Infrastructure.Persistence.Extensions;
using Leistd.Ddd.Infrastructure.EventBus;
using Leistd.EventBus.Local;
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
using static Leistd.ServiceClient.OAuth.DependencyInjection;
#endif
using CompanyName.ProjectName.Infrastructure.TenantConnections;
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
using CompanyName.ProjectName.Domain.Auth.Abstractions;
using CompanyName.ProjectName.Infrastructure.Auth.OAuth;
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
    /// 注册基础设施层服务。
    /// </summary>
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 所有上下文共用内存库命名和事务警告策略。
        void UseInMemoryFallback(DbContextOptionsBuilder options, string? suffix)
        {
            var databaseName = configuration["Database:InMemoryName"];
            var baseName = string.IsNullOrWhiteSpace(databaseName) ? "MyProject" : databaseName;
            options.UseInMemoryDatabase(suffix is null ? baseName : baseName + suffix);
            options.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning));
        }

        services.AddLocalEventBus();

        services.AddMemoryCache();
        var useExplicitInMemoryDatabase =
            !string.IsNullOrWhiteSpace(configuration["Database:InMemoryName"]);

        if (!useExplicitInMemoryDatabase)
        {
            // 真实数据库模式必须有共享库目标；在最终配置合并后执行启动校验。
            services.AddOptions<TenantConnectionResolutionOptions>()
                .Configure<IConfiguration>((options, config) =>
                {
                    options.DefaultConnectionString = config.GetConnectionString(ConnectionStringNames.Default);
                    options.InMemoryName = config["Database:InMemoryName"];
                })
                .Validate(
                    options => options.HasDatabaseTarget,
                    "No database target is configured. Set ConnectionStrings:Default, " +
                    "or set Database:InMemoryName to run against an in-memory store.")
                .ValidateOnStart();

#if (!LocalIdentity)
            // 远端解析：向 Identity 回源租户连接配置，按 TenantRouting:CacheLifetime 缓存（必须显式配置，
            // 它决定租户改路由前的排空等待）；同租户并发回源合并为一次。
            // 远端存储由框架提供，回源 Identity 经 MapTenantConnections 暴露的机器端点（配置节 Leistd:ServiceClients:Identity）。
            services.AddRemoteTenantConnectionResolution();
            var identityClient = services.AddRemoteTenantConnectionStore("Identity", configuration);
            if (configuration.GetSection(ServiceAuthSectionName).Exists())
            {
                identityClient.AddClientCredentials(configuration);
            }
            identityClient.AddStandardResilienceHandler();
#else
            // 本地解析：直接读本服务的控制库；控制库固定在宿主连接上，不参与租户路由。
            services.AddLocalTenantConnectionResolution<IdentityControlDbContext>(
                options => options.ControlPlaneConnectionStringName = IdentityControlDbContext.ConnectionStringName);
#endif
        }

#if (LocalIdentity)
        services.AddDbContext<IdentityControlDbContext>((sp, options) =>
        {
            // 控制面不继承 BaseDbContext，必须显式挂载修改和删除审计拦截器。
            options.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());

            var connectionString = configuration.GetControlPlaneConnectionString();
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                options.UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsHistoryTable(
                        DatabaseSchema.ControlMigrationsHistoryTable, DatabaseSchema.Name));
            }
            else
            {
                UseInMemoryFallback(options, "-control");
            }

        });
#endif

#if (OpenIddictServer)
        // OIDC 与控制面同库同 schema，但使用独立迁移历史。
        // 动态注入的 OIDC 实体不在快照中，因此只在此处抑制模型差异警告。
        services.AddDbContext<OpenIddictDbContext>(options =>
        {
            var connectionString = configuration.GetControlPlaneConnectionString();
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                options.UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsHistoryTable(
                        OpenIddictDbContext.MigrationsHistoryTable, DatabaseSchema.Name));
            }
            else
            {
                UseInMemoryFallback(options, "-openiddict");
            }

            options.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
            options.UseOpenIddict();
        });
#endif

        services.AddDbContext<MyProjectDbContext>((sp, options) =>
        {
            var creationContext = DbContextCreationContext.Current;
            var connectionString = creationContext?.ConnectionString ?? configuration.GetConnectionString(ConnectionStringNames.Default);
            if (!string.IsNullOrEmpty(connectionString))
            {
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
                    options.UseNpgsql(connectionString, ConfigureNpgsql);
                }
            }
            else
            {
                UseInMemoryFallback(options, suffix: null);
            }

            // SplitQuery 已处理多集合查询；模型与迁移不一致仍必须失败。
            options.ConfigureWarnings(w => w
                .Ignore(RelationalEventId.MultipleCollectionIncludeWarning));

            // 保存拦截器处理修改/删除审计、领域事件和并发标记。
            // 新增实体的环境值在进入 BaseDbContext 跟踪时落定。
            options.AddDddInterceptors(sp);
        });

#if (IncludeNotifications)
        services.AddNotificationsEfCore<MyProjectDbContext>();
        // 旧通知的保留期清理（默认开启：已读 90 天、未读 365 天，配置节 Leistd:Notifications:Retention）
        services.AddNotificationRetention<MyProjectDbContext>();
#endif
        services.AddAuthorizationEfCore<MyProjectDbContext>();
        services.AddSettingsEfCore<MyProjectDbContext>();
        services.AddOperationRecordsEfCore<MyProjectDbContext>();
        // 到期记录搬入归档表。默认关闭：审计表只增不减是安全的默认值，
        // 要启用就得有人显式打开（配置 Leistd:OperationRecords:Retention 或系统设置的「审计」面板）
        services.AddOperationRecordRetention<MyProjectDbContext>();
        // 集群周期任务的完成水位与业务表同库：多副本同一时段只跑一次
        services.AddBackgroundJobsEfCore<MyProjectDbContext>();
#if (LocalIdentity)
        // 租户注册表、连接登记与两个管理用例（租户管理、连接管理）都读写控制库
        services.AddMultiTenancyEfCore<IdentityControlDbContext>();
#endif

        services.AddDddInfrastructure();

        // 每个注册过的 DbContext 都必须显式接入：漏掉的上下文会逃出租户过滤器闸门，
        // 构建容器时会直接失败。不传选项即"只登记、不注册仓储"。
        services.AddDddDbContext<MyProjectDbContext>(options => options.AddDefaultRepositories());
#if (LocalIdentity)
        // 控制面上下文的租户注册表经自己的 Store 访问，不需要仓储。
        services.AddDddDbContext<IdentityControlDbContext>();
#endif
#if (OpenIddictServer)
        // OpenIddict 自有实体不是 Leistd 实体，只登记。
        services.AddDddDbContext<OpenIddictDbContext>();
#endif

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

            services.AddRedisDistributedLock(redisConnStr, configuration);
        }
        else
        {
            services.AddDistributedMemoryCache();
            services.AddMemoryLocalLock();
        }

        services.AddTransient<IPasswordHasher, PasswordHasher>();
#if (LocalIdentity)
        // 验证码摘要与口令哈希具有不同的密钥和成本契约。
        services.AddSingleton<IVerificationCodeDigest, HmacVerificationCodeDigest>();
#endif

#if (LocalIdentity)
        services.AddSmtpEmailSender(configuration);
#endif

#if (ExternalLogin)
        services.AddHttpClient();
        services.AddScoped<IOAuthProvider, GitHubOAuthProvider>();
        services.AddScoped<IOAuthProvider, GoogleOAuthProvider>();
#endif

        return services;
    }
}
