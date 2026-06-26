using Leistd.Auditing.EntityFrameworkCore;
using Leistd.Ddd.Infrastructure;
using Leistd.Ddd.Infrastructure.EventBus;
using Leistd.EventBus.Local;
using Leistd.Lock.Redis;
using Leistd.Lock.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using CompanyName.ProjectName.Infrastructure.Persistence;

using CompanyName.ProjectName.Domain.Shared.Security.Aes;
using CompanyName.ProjectName.Domain.Shared.Security.Aes.Options;
using CompanyName.ProjectName.Domain.Shared.Security.PasswordHash;
using CompanyName.ProjectName.Infrastructure.Shared.Security.Aes;
using CompanyName.ProjectName.Infrastructure.Shared.Security.PasswordHash;

#if (IncludeIdentity)
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

        // ✅ 注册 SaveChanges 拦截器（必须在 AddDbContext 之前）
        services.AddScoped<AuditSaveChangesInterceptor>();
        services.AddScoped<LocalEventSaveChangesInterceptor>();

        // ✅ 注册基础设施服务
        services.AddMemoryCache();

        services.Configure<EncryptionOptions>(configuration.GetSection(EncryptionOptions.SectionName));

        // ✅ 注册 DbContext（使用拦截器）
        services.AddDbContext<MyProjectDbContext>((sp, options) =>
        {
            var connectionString = configuration.GetConnectionString("Default");
            if (!string.IsNullOrEmpty(connectionString))
            {
                options.UseNpgsql(connectionString, npgsql =>
                    npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
            }
            else
            {
                options.UseInMemoryDatabase("MyProject");
            }

#if (IncludeOpenIddict)
            // 注册 OpenIddict EF Core 实体映射
            options.UseOpenIddict();
#endif

            // 抑制多集合 Include 警告（已全局启用 SplitQuery）
            // 抑制 PendingModelChangesWarning（OpenIddict 通过 UseOpenIddict() 动态注册实体，不在 Migration 快照中）
            options.ConfigureWarnings(w => w
                .Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId
                    .MultipleCollectionIncludeWarning)
                .Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId
                    .PendingModelChangesWarning));

            // ✅ 添加 SaveChanges 拦截器（EF Core 官方推荐的最佳实践）
            options.AddInterceptors(
                sp.GetRequiredService<AuditSaveChangesInterceptor>(),
                sp.GetRequiredService<LocalEventSaveChangesInterceptor>());
        });

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

#if (IncludeIdentity)
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
