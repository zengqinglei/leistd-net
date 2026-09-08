using Microsoft.Extensions.DependencyInjection;

namespace Leistd.DependencyInjection.Extensions;

/// <summary>
/// 「同一服务只能有一个权威实现」的注册期断言。
/// </summary>
public static class SingleAuthoritativeServiceExtensions
{
    /// <summary>
    /// 断言 <typeparamref name="TService"/> 还没有<b>别的</b>实现被注册；有则立刻抛错。
    /// </summary>
    /// <remarks>
    /// <para><b>只用于「多个实现说不通」的服务</b>——设置、通知历史、权限授予这类<b>存储</b>：
    /// 两个 <c>DbContext</c> 就是两个数据源，静默选中哪一个都不可接受。业务编排型服务
    /// （Manager 一类）不属于此列：那里「框架给默认实现、宿主可在组合根替换」是标准 DI 语义，
    /// 用 <c>TryAdd*</c> 表达即可，断言会把合法的替换也拒掉。</para>
    /// <para>泛型存储包（<c>AddXxxEfCore&lt;TDbContext&gt;()</c>）最容易撞上它：宿主为两个
    /// <c>DbContext</c> 各调一次，Microsoft DI 不报错，按单服务解析时静默取一条——数据写进
    /// 宿主没预期的库，读回来的也是那一个，全程没有信号。用 <c>TryAdd</c> 只是把
    /// 「最后一条胜出」换成「第一条胜出」，同样静默。</para>
    /// <para><b>实现类型与生命周期一起判定。</b>只比实现类型会留一条静默的错路径：宿主提前把
    /// 同一个实现登记成错误的生命周期，断言因"类型相同"放行，紧随其后的 <c>TryAdd*</c> 又因
    /// "已有注册"不再补正确的那条，最终框架保留了宿主那个错的。EF 存储被登记成单例尤其糟——
    /// 它会捕获作用域内的上下文。</para>
    /// <para><b>支持边界（刻意收窄）</b>：只判定<b>封闭类型注册</b>与<b>实例注册</b>——两者都能
    /// 确定实现身份。实例注册的生命周期恒为 <see cref="ServiceLifetime.Singleton"/>，因此只有
    /// <paramref name="expectedLifetime"/> 也是单例时才放行。工厂注册（<c>ImplementationFactory</c>）
    /// 无法证明它接出来的是什么，一律按冲突拒绝：对一个只能有一个权威实现的服务，
    /// 「不确定」不该当成「没问题」。keyed 注册排除在外，它按键解析、不参与单服务解析。
    /// 开放泛型注册（<c>typeof(IFoo&lt;&gt;)</c>）不在本方法范围内——
    /// <typeparamref name="TService"/> 是封闭类型，匹配不到那种描述符。</para>
    /// <para><b>必须枚举全部描述符，不能只看第一条</b>：单服务解析由最后一条胜出，
    /// 只看第一条会在「第一条恰是本类型、后面还有别的实现」时放行，而实际胜出的仍是后者。</para>
    /// <para>本方法只做断言。重复登记同一实现是幂等的，紧接着用 <c>TryAdd*</c> 即可。</para>
    /// </remarks>
    /// <typeparam name="TService">服务契约。</typeparam>
    /// <typeparam name="TImplementation">本次要登记的实现类型。</typeparam>
    /// <param name="services">服务集合。</param>
    /// <param name="expectedLifetime">本次要登记的生命周期；已有注册的生命周期必须与之一致。</param>
    /// <param name="reason">面向宿主的一句话，说明为什么这个服务只能有一个权威实现。</param>
    /// <exception cref="InvalidOperationException">
    /// 已存在另一个实现、同一实现但生命周期不同，或存在无法确定身份的工厂注册。
    /// </exception>
    public static IServiceCollection EnsureSingleAuthoritative<TService, TImplementation>(
        this IServiceCollection services,
        ServiceLifetime expectedLifetime,
        string reason)
        where TImplementation : class, TService
    {
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType != typeof(TService) || descriptor.IsKeyedService)
            {
                continue;
            }

            // 实例注册能问出真实类型；工厂注册问不出来，返回 null 按冲突处理。
            var registered = descriptor.ImplementationType ?? descriptor.ImplementationInstance?.GetType();
            if (registered == typeof(TImplementation) && descriptor.Lifetime == expectedLifetime)
            {
                continue;
            }

            var actual = registered?.FullName ?? "<factory>";
            throw new InvalidOperationException(
                $"An {typeof(TService).Name} is already registered as '{actual}' " +
                $"({descriptor.Lifetime}). {reason} Registering " +
                $"'{typeof(TImplementation).FullName}' ({expectedLifetime}) as well would silently win or " +
                "lose by ordering; and an existing registration with a different lifetime is kept as-is by " +
                "TryAdd, so the framework would end up honouring that one.");
        }

        return services;
    }
}
