using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Leistd.UnitOfWork.Options;
using Leistd.UnitOfWork.Attributes;
using Leistd.DependencyInjection;
using Leistd.EventBus.Abstractions;
using Leistd.EventBus.EventHandlers;
using Leistd.UnitOfWork.Events;
using Leistd.UnitOfWork.Interceptors;
using Leistd.UnitOfWork.Registration;
using Leistd.DependencyInjection.Extensions;
using Leistd.DependencyInjection.DynamicProxy.Extensions;
using Leistd.DependencyInjection.Abstractions;

namespace Leistd.UnitOfWork;

/// <summary>
/// 提供工作单元核心服务注册入口。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 获取配置节名称 <c>Leistd:UnitOfWork</c>。
    /// </summary>
    public const string ConfigurationSection = "Leistd:UnitOfWork";

    /// <summary>
    /// 注册工作单元核心服务并绑定默认配置节。
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddUnitOfWork(options =&gt;
    /// {
    ///     options.IsTransactional = true;
    ///     options.Timeout = TimeSpan.FromSeconds(30);
    /// });
    ///
    /// // 声明式边界（拦截器自动 Begin → SaveChanges → Commit）
    /// [UnitOfWork]
    /// public async Task PlaceOrderAsync(CreateOrderInput input) { /* ... */ }
    /// </code>
    /// </example>
    public static IServiceCollection AddUnitOfWork(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.Configure<UnitOfWorkOptions>(configuration.GetSection(ConfigurationSection));
        return services.AddUnitOfWorkCore();
    }

    /// <summary>
    /// 注册工作单元核心服务并使用委托配置选项。
    /// </summary>
    /// <remarks>
    /// 选项走 <see cref="Microsoft.Extensions.Options.IOptions{TOptions}"/>，
    /// 因此可配置绑定、可挂 <c>IValidateOptions</c> 与 <c>ValidateOnStart</c>。
    /// </remarks>
    public static IServiceCollection AddUnitOfWork(
        this IServiceCollection services,
        Action<UnitOfWorkOptions>? configureOptions = null)
    {
        if (configureOptions is not null)
        {
            services.Configure(configureOptions);
        }

        return services.AddUnitOfWorkCore();
    }

    private static IServiceCollection AddUnitOfWorkCore(this IServiceCollection services)
    {
        services.AddOptions<UnitOfWorkOptions>();


        services.TryAddSingleton<IAmbientUnitOfWork, AmbientUnitOfWork>();
        services.TryAddSingleton<IUnitOfWorkManager, UnitOfWorkManager>();
        services.TryAddTransient<IUnitOfWork, DefaultUnitOfWork>();
        services.TryAddTransient<UnitOfWorkInterceptor>();
        services.TryAddTransient<UnitOfWorkEventHandlerInterceptor>();

        // 活动工作单元内的事件交给阶段调度，而不是立即分发。
        services.TryAddSingleton<ILocalEventDeferrer, UnitOfWorkLocalEventDeferrer>();

        // 无法织入的声明必须在宿主启动时失败。
        services.AddRegistrationValidator(UnitOfWorkRegistrationValidator.Validate);

        services.OnServiceRegistered(context =>
        {
            if (ShouldInterceptUnitOfWork(context))
            {
                context.AddInterceptor(typeof(UnitOfWorkInterceptor));
            }
        });

        services.OnServiceRegistered(context =>
        {
            if (ShouldInterceptEventHandler(context))
            {
                context.AddInterceptor(typeof(UnitOfWorkEventHandlerInterceptor));
            }
        });

        return services;
    }

    private static bool ShouldInterceptUnitOfWork(IOnServiceRegisteredContext context)
    {
        var implementationType = context.ImplementationType;
        if (implementationType == null) return false;

        // Microsoft DI 不支持开放泛型服务类型与代理工厂组合。
        if (context.ServiceType.IsGenericTypeDefinition) return false;

        if (implementationType.GetCustomAttributes(typeof(UnitOfWorkAttribute), true).Any())
            return true;

        return implementationType.GetMethods()
            .Any(m => m.GetCustomAttributes(typeof(UnitOfWorkAttribute), true).Any());
    }

    // 所有事件处理器都必须织入，以便过滤 BeforeCommit 和 AfterCommit 阶段。
    private static bool ShouldInterceptEventHandler(IOnServiceRegisteredContext context)
    {
        if (!context.ServiceType.IsGenericType)
            return false;

        // 开放泛型无法织入，由注册校验器拒绝。
        if (context.ServiceType.IsGenericTypeDefinition)
            return false;

        return context.ServiceType.GetGenericTypeDefinition() == typeof(IEventHandler<>);
    }
}
