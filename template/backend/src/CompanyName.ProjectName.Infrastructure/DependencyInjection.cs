using Leistd.Auditing.EntityFrameworkCore;
using Leistd.Authorization.EntityFrameworkCore;
using Leistd.Settings.EntityFrameworkCore;
#if (LocalIdentity)
using Leistd.MultiTenancy.EntityFrameworkCore;
#endif
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
using Leistd.ServiceClient.OAuth;
using Leistd.ServiceClient.Refit;
using static Leistd.ServiceClient.OAuth.DependencyInjection;
#endif
using CompanyName.ProjectName.Infrastructure.TenantConnections;
#if (IncludeNotifications)
using Leistd.Notifications.EntityFrameworkCore;
using CompanyName.ProjectName.Infrastructure.Notifications;
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
using Leistd.Data.Constants;
using Leistd.Data.Abstractions;

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
            // 路由缓存期限决定租户改路由前的排空等待时间，必须显式配置。
            services.AddOptions<TenantRouteCacheOptions>()
                .Configure<IConfiguration>((options, config) =>
                {
                    var configured = config.GetSection(TenantRouteCacheOptions.SectionName)["CacheLifetime"];
                    if (TimeSpan.TryParse(configured, out var lifetime))
                    {
                        options.CacheLifetime = lifetime;
                    }
                })
                .Validate(
                    options => options.IsLifetimeUsable,
                    $"TenantRouting:CacheLifetime is required and must be greater than zero and at most " +
                    $"{TenantRouteCacheOptions.MaximumCacheLifetime}. It determines how long a changed tenant " +
                    "route may still be served by warm instances, and therefore how long the deactivate-and-drain " +
                    "step must wait before the route can be changed.")
                .ValidateOnStart();

            var identityClient = services.AddRefitServiceClient<
                IIdentityTenantConnectionClient,
                IdentityTenantConnectionClientOptions>("Identity", configuration);
            if (configuration.GetSection(ServiceAuthSectionName).Exists())
            {
                identityClient.AddClientCredentials(configuration);
            }
            identityClient.AddStandardResilienceHandler();

            // 宿主级单例跨请求合并同租户回源，且隔离同进程中的不同宿主。
            services.AddSingleton<TenantRouteResolutionCoordinator>();
            services.AddScoped<IConnectionStringResolver, IdentityTenantConnectionStringResolver>();
#else
            services.AddScoped<IConnectionStringResolver, LocalTenantConnectionStringResolver>();
#endif
        }
        services.AddSingleton<ISecretResolver, ConfigurationSecretResolver>();

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
        // 框架存储不提供批量删除，模板以专用服务实现通知清理。
        services.AddScoped<INotificationCleanupService, NotificationCleanupService>();
#endif
        services.AddAuthorizationEfCore<MyProjectDbContext>();
        services.AddSettingsEfCore<MyProjectDbContext>();
#if (LocalIdentity)
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

        var redisConnStr = configuration.GetConnectionString("Redis");

        if (!string.IsNullOrEmpty(redisConnStr))
        {
            var redisConfig = Shared.Redis.RedisConnectionStringParser.Parse(redisConnStr);

            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConfig));

            services.AddStackExchangeRedisCache(options =>
            {
                options.ConfigurationOptions = redisConfig;
                options.InstanceName = "MyProject:";
            });

            services.AddRedisDistributedLock(redisConfig.ToString(), configuration);
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

    /// <summary>
    /// 注册仅供 DbMigrator 使用的租户迁移目标枚举器。
    /// </summary>
    public static IServiceCollection AddTenantMigrationServices(this IServiceCollection services)
    {
        services.AddScoped<ITenantMigrationTargetProvider, TenantMigrationTargetProvider>();
        return services;
    }
}
