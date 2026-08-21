using Leistd.Auditing.EntityFrameworkCore;
#if (LocalAuthorization)
using Leistd.Authorization.EntityFrameworkCore;
#endif
#if (IdentityService)
using Leistd.MultiTenancy.EntityFrameworkCore;
#endif
using Leistd.Ddd.Infrastructure;
using Leistd.Ddd.Infrastructure.EventBus;
using Leistd.EventBus.Local;
using Leistd.Lock.Redis;
using Leistd.Lock.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Leistd.UnitOfWork.EfCore.Database;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using CompanyName.ProjectName.Infrastructure.Persistence;
#if (MultiTenancy)
using Leistd.MultiTenancy;
#endif
#if (ResourceService)
using Leistd.ServiceClient.OAuth;
using Leistd.ServiceClient.Refit;
#endif
using CompanyName.ProjectName.Infrastructure.TenantConnections;
#if (IncludeNotifications)
using Leistd.Notifications.EntityFrameworkCore;
using CompanyName.ProjectName.Infrastructure.Notifications;
#endif

using CompanyName.ProjectName.Domain.Shared.Security.Aes;
using CompanyName.ProjectName.Domain.Shared.Security.Aes.Options;
using CompanyName.ProjectName.Domain.Shared.Security.PasswordHash;
using CompanyName.ProjectName.Infrastructure.Shared.Security.Aes;
using CompanyName.ProjectName.Infrastructure.Shared.Security.PasswordHash;

#if (IdentityService)
using CompanyName.ProjectName.Domain.Shared.Email;
using CompanyName.ProjectName.Infrastructure.Email;
#endif
#if (IncludeExternalLogin)
using CompanyName.ProjectName.Domain.Auth.Abstractions;
using CompanyName.ProjectName.Infrastructure.Auth.OAuth;
#endif
using StackExchange.Redis;

namespace CompanyName.ProjectName.Infrastructure;

/// <summary>
/// Infrastructure 层依赖注入配置
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册 Infrastructure 层服务
    /// </summary>
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 注册本地事件总线
        services.AddLocalEventBus();

        // ✅ 注册基础设施服务
        services.AddMemoryCache();
        var useExplicitInMemoryDatabase =
            !string.IsNullOrWhiteSpace(configuration["Database:InMemoryName"]);

        if (!useExplicitInMemoryDatabase)
        {
#if (ResourceService)
            var identityClient = services.AddRefitServiceClient<
                IIdentityTenantConnectionClient,
                IdentityTenantConnectionClientOptions>("Identity", configuration);
            if (configuration.GetSection(Leistd.ServiceClient.OAuth.DependencyInjection.ServiceAuthSectionName).Exists())
            {
                identityClient.AddClientCredentials(configuration);
            }
            identityClient.AddStandardResilienceHandler();

            services.AddScoped<ITenantConnectionStringResolver, IdentityTenantConnectionStringResolver>();
#else
            services.AddScoped<ITenantConnectionStringResolver, LocalTenantConnectionStringResolver>();
#endif
        }
        services.AddSingleton<ISecretResolver, ConfigurationSecretResolver>();
        services.AddScoped<ITenantMigrationTargetProvider, TenantMigrationTargetProvider>();

        services.Configure<EncryptionOptions>(configuration.GetSection(EncryptionOptions.SectionName));

#if (IdentityService)
        services.AddDbContext<IdentityControlDbContext>((_, options) =>
        {
            var connectionString = configuration.GetConnectionString(IdentityControlDbContext.ConnectionStringName)
                ?? configuration.GetConnectionString("Default");
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                options.UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsHistoryTable("__EFMigrationsHistory_Control", "companyname-projectname"));
            }
            else
            {
                var databaseName = configuration["Database:InMemoryName"];
                options.UseInMemoryDatabase($"{(string.IsNullOrWhiteSpace(databaseName) ? "MyProject" : databaseName)}-control");
                options.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning));
            }

            options.UseOpenIddict();
        });
#endif

        // ✅ 注册租户业务 DbContext（使用拦截器）
        services.AddDbContext<MyProjectDbContext>((sp, options) =>
        {
            var creationContext = DbContextCreationContext.Current;
            var connectionString = creationContext?.ConnectionString ?? configuration.GetConnectionString("Default");
            if (!string.IsNullOrEmpty(connectionString))
            {
                void ConfigureNpgsql(NpgsqlDbContextOptionsBuilder npgsql)
                {
                    npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                    npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "companyname-projectname");
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
                var databaseName = configuration["Database:InMemoryName"];
                options.UseInMemoryDatabase(string.IsNullOrWhiteSpace(databaseName) ? "MyProject" : databaseName);
                options.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning));

            }

            // 抑制多集合 Include 警告（已全局启用 SplitQuery）
            // 抑制 PendingModelChangesWarning（OpenIddict 通过 UseOpenIddict() 动态注册实体，不在 Migration 快照中）
            options.ConfigureWarnings(w => w
                .Ignore(RelationalEventId.MultipleCollectionIncludeWarning)
                .Ignore(RelationalEventId.PendingModelChangesWarning));

            // 保存时刻的职责：修改/删除审计（含软删除转换）与领域事件发布。
            // 新增实体的环境值（CreatorId、CreationTime、TenantId）不在这里——
            // 由 BaseDbContext 在实体进入变更跟踪时落定，见框架 ddd-struct 文档
            options.AddInterceptors(
                sp.GetRequiredService<AuditSaveChangesInterceptor>(),
                sp.GetRequiredService<LocalEventSaveChangesInterceptor>());
        });

#if (IncludeNotifications)
        services.AddNotificationsEfCore<MyProjectDbContext>();
        // 通知“清空全部”能力：框架 INotificationStore 未提供删除，走自定义 EF 清理服务。
        services.AddScoped<INotificationCleanupService, NotificationCleanupService>();
#endif
#if (LocalAuthorization)
        services.AddAuthorizationEfCore<MyProjectDbContext>();
#endif
#if (IdentityService)
        // 租户注册表存储、管理器与落值拦截器（存储直接读库，除 DbContext 外无基础设施依赖）
        services.AddMultiTenancyEfCore<IdentityControlDbContext>();
#endif

        // 注册 DDD Infrastructure 基础服务（UnitOfWork + 自动仓储注册）
        services.AddDddInfrastructure();

        // 分布式缓存 + 分布式锁（优先 Redis，否则内存降级）
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

            services.AddRedisDistributedLock(redisConfig.ToString());
        }
        else
        {
            services.AddDistributedMemoryCache();
            services.AddMemoryLocalLock();
        }

        // 密码哈希服务（无状态，使用 Transient 生命周期）
        services.AddTransient<IPasswordHasher, PasswordHasher>();

        // AES 加密服务（无状态，使用 Transient 生命周期）
        services.AddTransient<IAesEncryptionProvider, AesEncryptionProvider>();

#if (IdentityService)
        // 邮件发送服务
        services.AddTransient<IEmailSender, MailKitEmailSender>();
#endif

#if (IncludeExternalLogin)
        // 外部认证 OAuth 提供商（Keyed DI）
        services.AddHttpClient();
        services.AddKeyedScoped<IOAuthProvider, GitHubOAuthProvider>("github");
        services.AddKeyedScoped<IOAuthProvider, GoogleOAuthProvider>("google");
#endif

        return services;
    }
}
