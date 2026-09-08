using System.Security.Claims;

namespace Leistd.AmbientContext;

/// <summary>
/// 环境上下文的一个维度：由拥有该维度的组件实现，在作用域内把它建立起来。
/// </summary>
/// <remarks>
/// 由各组件提供自己的维度，供已取得主体的后台任务、消息消费者与 Hub 调用组合。
/// HTTP 请求中，各维度仍由对应中间件按认证顺序建立。
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
