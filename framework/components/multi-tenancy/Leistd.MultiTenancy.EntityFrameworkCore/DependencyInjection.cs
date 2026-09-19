using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Leistd.Data.Abstractions;
using Leistd.DependencyInjection.Extensions;
using Leistd.Timing;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.MultiTenancy.EntityFrameworkCore.ConnectionStrings;
using Leistd.MultiTenancy.EntityFrameworkCore.EntityConfigurations;
using Leistd.MultiTenancy.EntityFrameworkCore.Managers;
using Leistd.MultiTenancy.EntityFrameworkCore.Stores;
using Leistd.MultiTenancy.Stores;

namespace Leistd.MultiTenancy.EntityFrameworkCore;

/// <summary>
/// 提供多租户 EF Core 存储注册和模型配置。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 使用指定 DbContext 注册租户注册表与租户连接的读写口。
    /// </summary>
    /// <remarks>
    /// <para>不注册租户落值组件；DDD 基座在实体进入跟踪时写入 <c>TenantId</c>。</para>
    /// <para>连接按 <c>(租户, 连接名)</c> 逐行登记：一个租户可以在 identity、foundation、crm 各有一条，
    /// 一条都没有即该租户不单独分库。</para>
    /// <para>连接的存储与写入口依赖 <c>IDataProtectionProvider</c>：连接串写入时加密、读取时解密。
    /// 宿主须自行 <c>AddDataProtection()</c> 并配置持久化、可共享的密钥环（本组件不管理密钥）；
    /// 读写同一控制库的所有进程（API、迁移作业）必须使用同一密钥环与应用名。</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddMultiTenancyEfCore&lt;ControlDbContext&gt;();
    ///
    /// // 控制面上下文没有软删除过滤器，查询必须使用未删除入口。
    /// var tenants = await controlDb.UndeletedTenants().ToListAsync(ct);
    /// </code>
    /// </example>
    public static IServiceCollection AddMultiTenancyEfCore<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        services.AddMultiTenancyCore();
        services.TryAddSingleton<IClock, UtcClockProvider>();
        services.TryAddTransient<ITenantStore, EfCoreTenantStore<TDbContext>>();
        services.TryAddTransient<ITenantManager, EfCoreTenantManager<TDbContext>>();
        // 连接配置只能有一个权威来源：控制库的 EF 实现，或资源服务回源控制面的 HTTP 实现。
        // 两者同为 ITenantConnectionConfigurationStore，同时注册会按顺序静默定胜负，
        // 而输的那一方决定的是"租户数据落在哪个库"。
        services.EnsureSingleAuthoritative<ITenantConnectionConfigurationStore,
            EfCoreTenantConnectionConfigurationStore<TDbContext>>(
            ServiceLifetime.Transient,
            "Tenant connection configuration must have exactly one authoritative source: either this control " +
            "database (AddMultiTenancyEfCore) or a remote control plane (AddRemoteTenantConnectionResolution " +
            "plus the host's HTTP implementation), never both.");
        services.TryAddTransient<ITenantConnectionConfigurationStore,
            EfCoreTenantConnectionConfigurationStore<TDbContext>>();
        services.TryAddTransient<ITenantConnectionConfigurationManager,
            EfCoreTenantConnectionConfigurationManager<TDbContext>>();
        return services;
    }

    /// <summary>
    /// 注册本地连接解析：宿主直连控制库，按 DbContext 的连接名查租户登记的连接。
    /// </summary>
    /// <typeparam name="TControlDbContext">映射了租户注册表（<see cref="ConfigureMultiTenancy"/>）的控制库上下文</typeparam>
    /// <param name="services">服务集合</param>
    /// <param name="configure">配置控制库连接名；启动期校验</param>
    /// <remarks>
    /// <para>解析用的名字<b>就是使用方 DbContext 的 <c>[ConnectionStringName]</c></b>，不需要额外配置。
    /// 三级落点：租户一条连接都没登记就用本服务自己的配置；登记了就按"精确名 → 默认名"取；
    /// 登记过却两者都没有则拒绝，不回落本服务的库。</para>
    /// <para>宿主须注册：控制库上下文（<c>AddDbContext</c>，直接注入、不经工作单元）；
    /// 迁移目标需要的 <see cref="AddMultiTenancyEfCore{TDbContext}"/>；
    /// 与写入方共享密钥环的 <c>AddDataProtection()</c>——连接串在这里解密，解不开即拒绝，不静默回落。</para>
    /// <para>同时注册 <see cref="ITenantMigrationTargetProvider"/>（读控制库）。均以 <c>TryAdd</c> 注册，宿主可替换。
    /// 连接配置在另一个服务时改用 Core 包的 <c>AddRemoteTenantConnectionResolution</c>，两者二选一。</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddLocalTenantConnectionResolution&lt;ControlDbContext&gt;(
    ///     options =&gt; options.ControlPlaneConnectionStringName = "IdentityControl");
    /// builder.Services.AddDataProtection()
    ///     .SetApplicationName("MyProject")
    ///     .PersistKeysToStackExchangeRedis(redis, "DataProtection-Keys");
    /// </code>
    /// </example>
    public static IServiceCollection AddLocalTenantConnectionResolution<TControlDbContext>(
        this IServiceCollection services,
        Action<LocalTenantConnectionOptions> configure)
        where TControlDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<LocalTenantConnectionOptions>, LocalTenantConnectionOptionsValidator>());
        services.AddOptions<LocalTenantConnectionOptions>().ValidateOnStart();

        services.TryAddScoped<IConnectionStringResolver, LocalConnectionStringResolver<TControlDbContext>>();
        services.TryAddTransient<ITenantMigrationTargetProvider, TenantMigrationTargetProvider>();

        return services;
    }

    /// <summary>
    /// 将租户注册表实体映射应用到模型。
    /// </summary>
    /// <remarks>
    /// 不添加查询过滤器；读取注册表必须使用 <c>TenantQueryableExtensions</c> 的未删除入口。
    /// </remarks>
    public static ModelBuilder ConfigureMultiTenancy(this ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new TenantRecordConfiguration());
        modelBuilder.ApplyConfiguration(new TenantConnectionRecordConfiguration());
        return modelBuilder;
    }
}
