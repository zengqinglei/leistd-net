namespace Leistd.BackgroundJobs.InProcess.Options;

/// <summary>
/// 进程内实现的调优参数。配置节 <c>Leistd:BackgroundJobs:InProcess</c>。
/// </summary>
public sealed class InProcessBackgroundJobOptions
{
    /// <summary>配置节路径。</summary>
    public const string SectionName = "Leistd:BackgroundJobs:InProcess";

    /// <summary>
    /// 后台任务队列容量，默认 256。
    /// </summary>
    /// <remarks>取值依据是"堆积到这个数就该让生产者慢下来"，调大只会让问题更晚暴露、堆积更多内存。</remarks>
    public int QueueCapacity { get; set; } = 256;
}
