namespace Leistd.Tracing.Abstractions;

/// <summary>
/// 当前关联标识的读取与临时切换入口。
/// </summary>
/// <remarks>
/// <para>关联标识是业务层面的标识：可以由调用方指定，也可以跨多条链路（一次操作发出的多个请求、重试、
/// 后台任务、补偿）。没有显式值时它等于当前 <see cref="System.Diagnostics.Activity"/> 的 TraceId，
/// 所以默认情况下日志、APM 与业务记录用的是同一个值。</para>
/// <para>宿主可替换默认实现，接入自己的请求号来源。</para>
/// </remarks>
public interface ICorrelationIdProvider
{
    /// <summary>
    /// 获取当前关联标识：显式切换的值优先，其次当前 Activity 的 TraceId；都没有时为 <see langword="null"/>。
    /// </summary>
    string? Get();

    /// <summary>
    /// 在返回的作用域内切换当前关联标识。
    /// </summary>
    /// <param name="correlationId">新的关联标识。</param>
    /// <returns>释放时恢复先前值的作用域；可嵌套。</returns>
    IDisposable Change(string correlationId);
}
