namespace Leistd.Timing;

/// <summary>
/// 提供当前时间。
/// </summary>
/// <remarks>
/// 框架的时间一律是 UTC：实体主键用时间有序的 <c>Guid.CreateVersion7()</c>、审计时间线、
/// 跨服务传递的 <c>DateTime</c> 都以此为共同基准。因此本接口<b>不提供切换时间类型的开关</b>——
/// 那会让上述保证各自失效，而且是静默的。按用户时区展示是呈现层的事，在边缘转换。
/// </remarks>
public interface IClock
{
    /// <summary>
    /// 获取当前 UTC 时间。
    /// </summary>
    DateTime Now { get; }

    /// <summary>
    /// 将时间归一化为 UTC。
    /// </summary>
    /// <param name="dateTime">要归一化的时间。</param>
    /// <returns>归一化后的 UTC 时间。</returns>
    DateTime Normalize(DateTime dateTime);
}
