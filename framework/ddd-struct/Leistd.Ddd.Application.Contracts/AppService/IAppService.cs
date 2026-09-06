namespace Leistd.Ddd.Application.Contracts.AppService;

/// <summary>
/// 应用服务的意图标记。
/// </summary>
/// <remarks>
/// <para><b>它不参与注册，也不参与织入。</b>本框架的注册一律显式手写
/// （<c>services.AddTransient&lt;IUserAppService, UserAppService&gt;()</c>）：注册集中在一处、
/// 可 grep、可推理，不存在"忘了标记就静默不注册"这类失效。横切行为由 AOP 特性 + 拦截器承担，
/// 拦截器的织入判据是特性（如 <c>[UnitOfWork]</c>），不是本接口。</para>
/// <para>它的作用只有一条：把"这个类型是应用服务"写在类型系统里，供人阅读与后续可能的分析器
/// 使用。<c>BaseAppService</c> 已实现它，继承基类即满足标记。</para>
/// </remarks>
public interface IAppService
{
}
