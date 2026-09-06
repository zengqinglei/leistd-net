using System.Security.Claims;

namespace Leistd.AmbientContext;

/// <summary>
/// 环境上下文的一个维度：由拥有该维度的组件实现，在作用域内把它建立起来。
/// </summary>
/// <remarks>
/// <para>抽象放在零依赖的 <c>Leistd.Core</c>：它是跨维度的组合点，任何一个维度所在的组件
/// 都不该拥有它。实现由各自的组件提供（多租户组件建立租户、追踪组件建立链路标识），
/// 因此不产生任何新的包依赖边——各组件本来就依赖 <c>Leistd.Core</c>。</para>
/// <para><b>它服务的是"拿到完整身份才进入"的入口</b>——SignalR Hub 方法调用、
/// 后台作业、消息消费者。HTTP 请求不走这里：它是渐进发现身份的管道，
/// 链路标识要在认证之前建立、租户要在主体确定之后建立，
/// 三个维度分处三个管道阶段且顺序受力，塞进一次调用会破坏其中一条。</para>
/// </remarks>
public interface IAmbientContextContributor
{
    /// <summary>
    /// 建立本维度，返回退出作用域时的还原句柄；本维度不适用时返回 <see langword="null"/>。
    /// </summary>
    IDisposable? Enter(AmbientContextEnterContext context);
}

/// <summary>
/// 建立环境上下文时交给各维度的输入。
/// </summary>
/// <param name="Principal">本次调用的主体。调用方保证非空。</param>
/// <param name="CorrelationId">
/// 指定的链路标识；为 <see langword="null"/> 时由追踪维度自行决定
/// （有 <see cref="System.Diagnostics.Activity"/> 取其 TraceId，否则新建）。
/// </param>
public sealed record AmbientContextEnterContext(
    ClaimsPrincipal Principal,
    string? CorrelationId = null);
