using Leistd.AmbientContext;
using Leistd.Tracing.AmbientContext;
using Leistd.Tracing.Attributes;
using Leistd.Tracing.Interceptors;
using Leistd.Tracing.Options;
using Leistd.Tracing.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Leistd.DependencyInjection.Extensions;
using Leistd.DependencyInjection.DynamicProxy.Extensions;
using Leistd.Tracing.Abstractions;

namespace Leistd.Tracing;

/// <summary>
/// 链路追踪核心能力的注册入口：<c>ICorrelationIdProvider</c> 与 <c>[CorrelationId]</c> 拦截器。
/// </summary>
public static class DependencyInjection
{
    /// <summary>注册链路追踪核心能力（不含 ASP.NET Core 中间件），选项绑定自配置节。</summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">承载 <c>CorrelationIdOptions</c> 配置节的配置根。</param>
    /// <example>
    /// <code>
    /// builder.Services.AddCorrelationIdCore(builder.Configuration);
    ///
    /// // 无 HTTP 入口的方法用特性开启作用域（需 DynamicProxy 织入）
    /// [CorrelationId]
    /// public async Task RunAsync(CancellationToken ct) { /* ... */ }
    /// </code>
    /// </example>
    public static IServiceCollection AddCorrelationIdCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<CorrelationIdOptions>(configuration.GetSection("Leistd:CorrelationId"));
        return AddCorrelationIdCore(services);
    }

    /// <summary>注册链路追踪核心能力（不含 ASP.NET Core 中间件），选项以委托配置。</summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configureOptions">选项配置委托。</param>
    public static IServiceCollection AddCorrelationIdCore(
        this IServiceCollection services,
        Action<CorrelationIdOptions> configureOptions)
    {
        services.Configure(configureOptions);
        return AddCorrelationIdCore(services);
    }

    private static IServiceCollection AddCorrelationIdCore(IServiceCollection services)
    {
        services.PostConfigure<CorrelationIdOptions>(options =>
        {
            options.HeaderNames = [.. (options.HeaderNames ?? [])
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)];
        });
        services.AddOptions<CorrelationIdOptions>()
            .Validate(options => options.HeaderNames.Length > 0,
                "At least one non-empty correlation id header name is required.")
            .ValidateOnStart();

        services.TryAddSingleton<ICorrelationIdProvider, CorrelationIdProvider>();

        // 非 HTTP 入口的链路标识维度：Hub 调用、后台作业不经入站中间件，
        // 由环境上下文原语建立，优先级与中间件同口径（Activity 优先）。
        services.TryAddEnumerable(
            ServiceDescriptor.Transient<IAmbientContextContributor, CorrelationIdAmbientContextContributor>());

        services.TryAddTransient<CorrelationIdInterceptor>();

        // 注册服务回调，扫描带有 [CorrelationId] 特性的服务
        services.OnServiceRegistered(context =>
        {
            if (ShouldIntercept(context.ImplementationType))
            {
                context.AddInterceptor(typeof(CorrelationIdInterceptor));
            }
        });

        return services;
    }

    // 工厂委托注册拿不到实现类型（为 null），本判定完全依赖类/方法上的特性，因此不处理
    private static bool ShouldIntercept(Type? implementationType)
    {
        if (implementationType == null) return false;

        // 检查类特性
        if (implementationType.GetCustomAttributes(typeof(CorrelationIdAttribute), true).Any())
            return true;

        // 检查方法特性
        return implementationType.GetMethods()
            .Any(m => m.GetCustomAttributes(typeof(CorrelationIdAttribute), true).Any());
    }
}
