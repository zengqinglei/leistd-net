using Leistd.Ddd.Application.Contracts.AppService;

namespace Leistd.Ddd.Application.AppService;

/// <summary>
/// 应用服务基类，同时承载 <see cref="IAppService"/> 约定标记。
/// </summary>
/// <remarks>
/// <para><b>它是刻意预留的扩展缝，不是当前就有共享行为。</b>「继承」这个动作写在<b>生成后的项目
/// 代码</b>里，而本类住在框架包里：只有生成代码已经继承，框架才能在后续版本里下发应用服务层的
/// 公共行为，靠升级包生效。删掉它等于关掉这条推送通道——已生成的项目模板改不到，只能逐个回改。</para>
/// <para>继承本类即满足 <see cref="IAppService"/>，派生类不必重复声明标记。
/// 该标记<b>不参与注册与织入</b>——注册一律显式手写，理由见 <see cref="IAppService"/>。</para>
/// <para><b>可以往这里加什么</b>：不需要注入状态的东西——<c>protected</c> 帮助方法、
/// 模板方法钩子、约定常量。</para>
/// <para><b>不可以加什么</b>：<c>IServiceProvider</c> 或任何延迟服务定位器（形如
/// <c>LazyServiceProvider</c> 暴露时钟、映射器、当前用户……）。那是服务定位器，
/// 会把真实依赖从构造签名里藏起来，单元测试无法从签名看出要准备什么，
/// 与 .NET 依赖注入指南明确列为反模式的做法一致。应用服务需要什么就在自己的构造函数里声明。</para>
/// <para>横切关注点（事务、审计、授权、追踪）由 AOP 拦截器 + 特性承担，不走继承。</para>
/// </remarks>
public abstract class BaseAppService : IAppService
{
}
