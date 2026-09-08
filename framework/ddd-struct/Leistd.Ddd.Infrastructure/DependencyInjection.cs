using System.Reflection;
using Leistd.Auditing.EntityFrameworkCore;
using Leistd.Ddd.Domain.DataFilters;
using Leistd.Ddd.Domain.Entities;
using Leistd.Ddd.Domain.Repositories;
using Leistd.Ddd.Infrastructure.EventBus;
using Leistd.Ddd.Infrastructure.Persistence.Interceptors;
using Leistd.Ddd.Infrastructure.HostedServices;
using Leistd.UnitOfWork;
using Leistd.UnitOfWork.Options;
using Leistd.UnitOfWork.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Leistd.Ddd.Infrastructure.Persistence.Repositories;
using Leistd.Timing;
using Leistd.DependencyInjection.Extensions;
using Leistd.DependencyInjection.DynamicProxy.Extensions;

namespace Leistd.Ddd.Infrastructure;

/// <summary>
/// 提供 DDD 基础设施服务注册。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册工作单元、数据过滤和 DbContext 仓储等 DDD 基础设施。
    /// </summary>
    /// <example>
    /// <code>
    /// // 必要步骤：拦截器织入与漏登记校验都由这个工厂驱动，不装则两者都不生效。
    /// builder.Host.UseServiceProviderFactory(new DynamicProxyServiceRegistrationCallbackFactory());
    ///
    /// builder.Services.AddDddInfrastructure();
    /// builder.Services.AddDddDbContext&lt;AppDbContext&gt;(o =&gt; o.AddDefaultRepositories());
    ///
    /// // 修改/删除审计与领域事件需要显式挂载拦截器。
    /// builder.Services.AddDbContext&lt;AppDbContext&gt;((sp, options) =&gt; options
    ///     .UseNpgsql(connectionString)
    ///     .AddDddInterceptors(sp));
    /// </code>
    /// </example>
    public static IServiceCollection AddDddInfrastructure(
        this IServiceCollection services,
        Action<UnitOfWorkOptions>? configureUnitOfWork = null)
    {
        services.AddSingleton<IQueryableAsyncExecuter, EfCoreQueryableAsyncExecuter>();

        // 保留宿主或测试预先注册的时钟实现。
        services.TryAddSingleton<IClock, UtcClockProvider>();

        services.AddAuditingEfCore();

        services.AddScoped<LocalEventSaveChangesInterceptor>();

        services.TryAddSingleton<ConcurrencyStampSaveChangesInterceptor>();

        services.AddSingleton<IDataFilter, DataFilter>(); // 非泛型版本，单例
        services.AddSingleton(typeof(IDataFilter<>), typeof(DataFilter<>)); // 状态由 AsyncLocal 隔离

        services.AddUnitOfWork(configureUnitOfWork);

        services.AddUnitOfWorkEfCore();

        // 上下文清单由 AddDddDbContext<TDbContext>() 显式登记，不再扫描容器。
        GetOrCreateTrackedDbContextTypes(services);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, MultiTenantFilterGuard>());

        // 显式登记换来一个新的"忘写"模式：注册了 DbContext 却没调 AddDddDbContext。
        // 后果是这个上下文逃出租户过滤器闸门——一道防跨租户泄漏的检查静默不覆盖它。
        // 校验器由 Leistd 的 IServiceProviderFactory 在构建时执行。宿主用裸
        // BuildServiceProvider() 时它根本不跑，框架也就查不出漏登记：闸门只看已登记的上下文，
        // 漏掉的那个自始至终不可见。因此该 Factory 是完整 DDD 组合的必要步骤，不是可选优化。
        services.AddRegistrationValidator(EnsureEveryDbContextIsDeclared);

        return services;
    }

    /// <summary>
    /// 把一个 DbContext 接入 DDD 基础设施：登记进租户过滤器闸门，并按选项注册仓储。
    /// </summary>
    /// <remarks>
    /// <para><b>每个注册过的 DbContext 都必须调用一次</b>，包括不需要仓储的——
    /// 漏掉的上下文会逃出租户过滤器闸门。宿主装了 Leistd 的
    /// <c>IServiceProviderFactory</c>（框架的拦截器织入本来就依赖它）时，漏写在构建容器时
    /// 直接失败；<b>不装则框架查不出漏登记</b>——校验器不执行，闸门也只看已登记的上下文。
    /// 该 Factory 因此是完整 DDD 组合的必要步骤。</para>
    /// <para>仓储是显式开关：不传选项即"只登记、不注册仓储"，适用于控制面这类
    /// 只需要工作单元与闸门的上下文。</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // 业务上下文：要仓储
    /// builder.Services.AddDddDbContext&lt;AppDbContext&gt;(o =&gt; o.AddDefaultRepositories());
    ///
    /// // 控制面上下文：只登记
    /// builder.Services.AddDddDbContext&lt;ControlPlaneDbContext&gt;();
    /// </code>
    /// </example>
    /// <typeparam name="TDbContext">已通过 <c>AddDbContext</c> 注册的上下文类型。</typeparam>
    /// <param name="services">服务集合。</param>
    /// <param name="configure">仓储注册选项；省略时只登记上下文。</param>
    public static IServiceCollection AddDddDbContext<TDbContext>(
        this IServiceCollection services,
        Action<DddDbContextOptions>? configure = null)
        where TDbContext : DbContext
    {
        var options = new DddDbContextOptions();
        configure?.Invoke(options);

        GetOrCreateTrackedDbContextTypes(services).Add(typeof(TDbContext));
        RegisterRepositories(services, typeof(TDbContext), options);

        return services;
    }

    // 重复注册时复用同一清单，避免不同实例各自只收集部分上下文。
    private static TrackedDbContextTypes GetOrCreateTrackedDbContextTypes(IServiceCollection services)
    {
        if (services.FirstOrDefault(d => d.ServiceType == typeof(TrackedDbContextTypes))
                ?.ImplementationInstance is TrackedDbContextTypes existing)
        {
            return existing;
        }

        var tracked = new TrackedDbContextTypes();
        services.AddSingleton(tracked);
        return tracked;
    }

    // 本上下文按 DbSet<T> 声明派生的实体。注册阶段拿不到 EF Core 模型
    // （取 Model 要实例化 DbContext，而那需要已构建的容器），只能反射类型。
    private static IEnumerable<Type> DiscoverEntityTypes(Type dbContextType) =>
        dbContextType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType.IsGenericType &&
                        p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
            .Select(p => p.PropertyType.GetGenericArguments()[0])
            .Where(t => typeof(IEntity).IsAssignableFrom(t));

    // 先把「实体 → 实现」定案再落注册：同一上下文内 AddDefaultRepositories 与
    // AddDefaultRepository 重叠时自然去重，且自定义实现恒优先。
    private static Dictionary<Type, Type> ResolveRepositoryPlan(
        Type dbContextType,
        DddDbContextOptions options)
    {
        var plan = new Dictionary<Type, Type>(options.CustomRepositories);

        var defaults = options.RegisterDefaultRepositories
            ? DiscoverEntityTypes(dbContextType).Concat(options.ExplicitEntities)
            : options.ExplicitEntities;

        foreach (var entityType in defaults)
        {
            if (plan.ContainsKey(entityType))
            {
                continue;
            }

            var keyType = FindPrimaryKeyType(entityType);
            plan[entityType] = keyType is null
                ? typeof(EfCoreRepository<,>).MakeGenericType(dbContextType, entityType)
                : typeof(EfCoreRepository<,,>).MakeGenericType(dbContextType, entityType, keyType);
        }

        return plan;
    }

    private static void RegisterRepositories(
        IServiceCollection services,
        Type dbContextType,
        DddDbContextOptions options)
    {
        foreach (var (entityType, implementationType) in ResolveRepositoryPlan(dbContextType, options))
        {
            AddRepositoryDescriptor(services, typeof(IRepository<>).MakeGenericType(entityType), implementationType, entityType);

            if (FindPrimaryKeyType(entityType) is { } keyType)
            {
                var keyedInterface = typeof(IRepository<,>).MakeGenericType(entityType, keyType);

                // AddRepository 的泛型约束只能表达 IRepository<TEntity>——TKey 不在它的签名里。
                // 带主键的实体这里还会注册 IRepository<TEntity,TKey>，只实现无主键接口的自定义
                // 仓储能编译通过却生成不可赋值的描述符，要到 ValidateOnBuild 才炸且信息含糊。
                // 显式点名注册点错了就该当场说清楚。
                if (!keyedInterface.IsAssignableFrom(implementationType))
                {
                    throw new InvalidOperationException(
                        $"Repository '{implementationType.FullName}' registered for entity " +
                        $"'{entityType.FullName}' does not implement " +
                        $"'IRepository<{entityType.Name}, {keyType.Name}>'. Entities with a primary key " +
                        "are registered under both the keyed and the non-keyed repository interface, so " +
                        "the implementation must provide both.");
                }

                AddRepositoryDescriptor(services, keyedInterface, implementationType, entityType);
            }
        }
    }

    // 同一实体被两个上下文各注册一次时，Microsoft DI 让后注册的静默胜出——
    // 调用方拿到的是哪个库的仓储由注册顺序决定，且没有任何信号。此处直接拒绝。
    private static void AddRepositoryDescriptor(
        IServiceCollection services,
        Type repositoryInterface,
        Type implementationType,
        Type entityType)
    {
        if (services.FirstOrDefault(d => d.ServiceType == repositoryInterface) is { } existing)
        {
            throw new InvalidOperationException(
                $"A repository for '{entityType.FullName}' is already registered as " +
                $"'{existing.ImplementationType?.FullName ?? "<factory>"}'. Registering " +
                $"'{implementationType.FullName}' would silently win by ordering and the caller " +
                "could not tell which database it reads. Map the entity in one DbContext, or use " +
                $"{nameof(DddDbContextOptions.AddRepository)} to name the implementation explicitly.");
        }

        services.AddScoped(repositoryInterface, implementationType);
    }

    // 注册了 DbContext 却没有显式接入的，直接让宿主起不来：那个上下文会逃出
    // 租户过滤器闸门。不要仓储的上下文调用无参重载即可（只登记，不注册仓储）。
    private static void EnsureEveryDbContextIsDeclared(IServiceCollection services)
    {
        if (services.FirstOrDefault(d => d.ServiceType == typeof(TrackedDbContextTypes))
                ?.ImplementationInstance is not TrackedDbContextTypes tracked)
        {
            return;
        }

        var undeclared = services
            .Select(d => d.ServiceType)
            .Where(t => typeof(DbContext).IsAssignableFrom(t) && t != typeof(DbContext) && !t.IsAbstract)
            .Distinct()
            .Where(t => !tracked.Types.Contains(t))
            .Select(t => t.FullName)
            .Order(StringComparer.Ordinal)
            .ToList();

        if (undeclared.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"These DbContext types are registered but never passed to AddDddDbContext<TDbContext>(): " +
            $"{string.Join(", ", undeclared)}. They would escape the multi-tenant filter guard, which " +
            "is what stops multi-tenant entities from being mapped into a context with no tenant filter. " +
            "Call AddDddDbContext<TDbContext>() for each of them; pass no options when the context only " +
            "needs the unit of work and the guard.");
    }

    private static Type? FindPrimaryKeyType(Type entityType)
    {
        var entityInterface = entityType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType &&
                                 i.GetGenericTypeDefinition() == typeof(IEntity<>));
        return entityInterface?.GetGenericArguments()[0];
    }
}
