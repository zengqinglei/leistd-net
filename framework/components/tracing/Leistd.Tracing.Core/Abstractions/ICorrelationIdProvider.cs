namespace Leistd.Tracing.Abstractions;

/// <summary>
/// 当前链路标识的读取与临时切换入口。
/// </summary>
/// <remarks>
/// 有 <see cref="System.Diagnostics.Activity"/> 时以它的 TraceId 为准；无 Activity 时生成 W3C 形态的标识。
/// </remarks>
public interface ICorrelationIdProvider
{
    /// <summary>
    /// 获取当前 TraceId。
    /// </summary>
    string? Get();

    /// <summary>
    /// 创建 32 位十六进制 TraceId。
    /// </summary>
    string Create();

    /// <summary>
    /// 在返回的作用域内切换当前 TraceId。
    /// </summary>
    /// <param name="correlationId">新的 TraceId。</param>
    /// <returns>释放时恢复先前值的作用域。</returns>
    IDisposable Change(string correlationId);
}
