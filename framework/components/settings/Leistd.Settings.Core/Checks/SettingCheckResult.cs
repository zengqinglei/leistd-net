namespace Leistd.Settings.Checks;

/// <summary>某一租户（宿主上下文为宿主）下一项测试最近一次的结果。</summary>
/// <param name="Passed">是否通过。</param>
/// <param name="FailureCode">未通过时的失败码。</param>
/// <param name="Time">测试时间（UTC）。</param>
/// <param name="CheckerId">测试人标识；无主体时为 <see langword="null"/>。</param>
/// <param name="CheckerName">测试人显示名快照；无主体时为 <see langword="null"/>。</param>
/// <param name="IsStale">测试之后，主设置或依赖设置在同一租户里被写入或清除过，结论可能已不反映当前配置。</param>
public sealed record SettingCheckResult(
    bool Passed,
    string? FailureCode,
    DateTime Time,
    string? CheckerId,
    string? CheckerName,
    bool IsStale);

/// <summary>测试结果的持久化契约，按当前租户隐式限定（宿主上下文即宿主）。</summary>
/// <remarks>
/// 须经当前工作单元读写：写入设置时标记过期与设置值的写入在同一事务里提交。
/// 永久废弃租户时由 <see cref="Stores.ISettingStore.RemoveAllAsync"/> 的实现一并清理。
/// </remarks>
public interface ISettingCheckStore
{
    /// <summary>读取当前租户下全部测试的最近结果，键为主设置名。</summary>
    Task<IReadOnlyDictionary<string, SettingCheckResult>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>保存运行 <paramref name="sequence"/> 的结果，覆盖上一次（含过期标记，按 <see cref="SettingCheckResult.IsStale"/> 原样保存）。</summary>
    /// <remarks>调用方持有设置写锁，并已确认没有更新的运行保存过结果。</remarks>
    Task SaveAsync(string settingName, long sequence, SettingCheckResult result, CancellationToken cancellationToken = default);

    /// <summary>
    /// 主设置或依赖设置被写入、清除：把当前租户下这些测试已有的结果标记为过期，并推进它们的配置修订号
    /// （见 <see cref="BeginRunAsync"/>）。与设置写入在同一事务里调用；从没测过的项忽略。
    /// </summary>
    Task MarkStaleAsync(IReadOnlyCollection<string> settingNames, CancellationToken cancellationToken = default);

    /// <summary>登记一次即将开始的运行：分配递增的运行序号，并记下此刻的配置修订号；已有的最近结果保持不变。</summary>
    /// <remarks>调用方持有设置写锁，序号的分配与配置写入互相串行。</remarks>
    Task<SettingCheckRun> BeginRunAsync(string settingName, CancellationToken cancellationToken = default);

    /// <summary>读取一项测试此刻的配置修订号与已保存结果的运行序号；从没登记过时均为 0。</summary>
    Task<SettingCheckRun> GetLatestAsync(string settingName, CancellationToken cancellationToken = default);
}

/// <summary>一项测试的运行计数。</summary>
/// <param name="Sequence">运行序号：每次开始测试时递增；读取最新时为已保存结果的序号。</param>
/// <param name="Revision">配置修订号：主设置或依赖设置每被写入、清除一次就递增。</param>
public readonly record struct SettingCheckRun(long Sequence, long Revision);

/// <summary>一项测试的登记：挂在哪个设置上、结论依赖哪些设置、由哪个探针执行。</summary>
/// <param name="SettingName">主设置：界面上测试按钮所在的那一项。</param>
/// <param name="DependsOn">结论依赖的其他设置；它们与主设置任一项被写入或清除，结果即过期。</param>
/// <param name="CheckType">探针类型，实现 <see cref="ISettingCheck"/>。</param>
public sealed record SettingCheckRegistration(string SettingName, IReadOnlyList<string> DependsOn, Type CheckType)
{
    /// <summary>主设置与依赖设置。</summary>
    public IEnumerable<string> Covers => DependsOn.Prepend(SettingName);
}
