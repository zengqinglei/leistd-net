namespace Leistd.BackgroundJobs.Recurring;

/// <summary>
/// 表示宿主已注册周期任务的调度器实现。
/// </summary>
/// <remarks>
/// 调度器实现（如 <c>AddInProcessBackgroundJobs()</c>）注册时登记本标记。登记了周期任务却没有调度器时，
/// 任务永远不会执行且不报错；<c>AddRecurringJob</c> 注册的启动检查据此在日志里告警。
/// </remarks>
public sealed class RecurringJobSchedulerMarker;
