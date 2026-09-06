using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Leistd.TestBase.Assertions;

/// <summary>
/// 针对 <see cref="IServiceCollection"/> 的断言词汇：把"注册面正确"写成一行。
/// </summary>
/// <remarks>
/// <para>Leistd 靠宿主显式调用 <c>AddXxx()</c> 组合，没有模块图也没有约定注册——
/// 注册结果就是公共契约的一部分，且是显式组合模型的主要风险面：
/// 生命周期写错、重复注册、组件之间意外互相覆盖，编译期一个都发现不了。</para>
/// <para>规范：每个 <c>DependencyInjection.cs</c> 至少三条用例——注册结果与生命周期、
/// 重复调用幂等、与相邻组件的覆盖/共存关系。</para>
/// </remarks>
public static class ServiceCollectionAssertions
{
    /// <summary>断言 <typeparamref name="TService"/> 恰好注册一次，且生命周期符合预期。</summary>
    public static ServiceDescriptor AssertSingle<TService>(
        this IServiceCollection services, ServiceLifetime lifetime)
    {
        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(TService));
        Assert.Equal(lifetime, descriptor.Lifetime);
        return descriptor;
    }

    /// <summary>断言 <typeparamref name="TService"/> 恰好注册一次，且由 <typeparamref name="TImplementation"/> 兑现。</summary>
    /// <remarks>
    /// 工厂注册（<c>ImplementationFactory</c>）拿不到实现类型，此时改为解析后断言实例类型——
    /// 本方法只覆盖按类型注册的情形，工厂注册请用 <see cref="AssertResolvesTo{TService, TImplementation}"/>。
    /// </remarks>
    public static void AssertImplementedBy<TService, TImplementation>(this IServiceCollection services)
    {
        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(TService));
        Assert.Equal(typeof(TImplementation), descriptor.ImplementationType);
    }

    /// <summary>构建容器并断言 <typeparamref name="TService"/> 解析为 <typeparamref name="TImplementation"/>。</summary>
    /// <remarks>覆盖工厂注册与"后注册覆盖先注册"这两种描述符层面看不出结果的情形。</remarks>
    public static void AssertResolvesTo<TService, TImplementation>(this IServiceCollection services)
        where TService : notnull
    {
        using var provider = services.BuildServiceProvider();
        Assert.IsType<TImplementation>(provider.GetRequiredService<TService>());
    }

    /// <summary>断言 <typeparamref name="TService"/> 未被注册。</summary>
    /// <remarks>
    /// 用于钉住"本组件不替其他组件注册基础设施"——那是设计原则里的硬边界，
    /// 破坏它的症状（宿主拿到一个它没要过的实现）在集成阶段才会显形。
    /// </remarks>
    public static void AssertNotRegistered<TService>(this IServiceCollection services) =>
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(TService));

    /// <summary>断言同一注册动作执行两次后，不产生重复的服务注册。</summary>
    /// <example>
    /// <code>
    /// ServiceCollectionAssertions.AssertIdempotent(services => services.AddSecurity());
    /// </code>
    /// </example>
    /// <remarks>
    /// <para>宿主重复调用 <c>AddXxx()</c> 是常态（组合根拆分、组件互相"确保已注册"）。
    /// 不幂等的表现是同一接口被解析成 <c>IEnumerable</c> 时出现重复项，或单例被建两份。</para>
    /// <para><b>选项配置器不计入</b>：<c>IConfigureOptions&lt;&gt;</c> 一族按 Microsoft.Extensions.Options
    /// 的契约就是累加的，同一个配置委托跑两遍对结果没有影响。把它们算进来会让任何经
    /// <c>AddOptions&lt;&gt;()</c> 或 <c>AddSignalR()</c> 的注册面永远无法通过本断言，
    /// 于是这条规则会被整体放弃——那才是真正的损失。</para>
    /// </remarks>
    public static void AssertIdempotent(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();

        register(services);
        var afterFirst = Meaningful(services);

        register(services);

        var duplicates = Meaningful(services)
            .GroupBy(d => d)
            .Where(g => g.Count() > afterFirst.Count(d => d.Equals(g.Key)))
            .Select(g => g.Key)
            .ToArray();

        Assert.True(
            duplicates.Length == 0,
            "重复调用产生了新的服务注册：" + string.Join("；", duplicates.Select(d => $"{d.Service} -> {d.Implementation}")));

        static List<(string Service, string Implementation)> Meaningful(IServiceCollection services) =>
            [.. services
                .Where(d => !IsOptionsConfigurator(d.ServiceType))
                .Select(d => (
                    d.ServiceType.FullName ?? d.ServiceType.Name,
                    d.ImplementationType?.FullName
                        ?? d.ImplementationInstance?.GetType().FullName
                        ?? "<factory>"))];

        static bool IsOptionsConfigurator(Type serviceType) =>
            serviceType.IsGenericType
            && serviceType.GetGenericTypeDefinition() is var open
            && (open == typeof(IConfigureOptions<>)
                || open == typeof(IPostConfigureOptions<>)
                || open == typeof(IValidateOptions<>)
                || open == typeof(IOptionsChangeTokenSource<>));
    }
}
