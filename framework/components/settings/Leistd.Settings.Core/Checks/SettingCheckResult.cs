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

    /// <summary>保存一项测试的最近结果，覆盖上一次（含过期标记，按 <see cref="SettingCheckResult.IsStale"/> 原样保存），并清除运行登记。</summary>
    Task SaveAsync(string settingName, SettingCheckResult result, CancellationToken cancellationToken = default);

    /// <summary>
    /// 把当前租户下这些测试的结果标记为过期，并作废它们正在进行的运行登记（见 <see cref="BeginRunAsync"/>）。
    /// 与设置写入在同一事务里调用；既没有结果也没有登记的项忽略。
    /// </summary>
    Task MarkStaleAsync(IReadOnlyCollection<string> settingNames, CancellationToken cancellationToken = default);

    /// <summary>登记一次即将开始的运行，返回登记标识；已有的最近结果保持不变，之前的登记被接替。</summary>
    /// <remarks>调用方持有设置写锁；登记在 <see cref="MarkStaleAsync"/> 或下一次登记之前一直有效，<see cref="SaveAsync"/> 后清除。</remarks>
    Task<Guid> BeginRunAsync(string settingName, CancellationToken cancellationToken = default);

    /// <summary>读取一项测试当前有效的运行登记；没有或已被作废时为 <see langword="null"/>。</summary>
    Task<Guid?> GetPendingRunAsync(string settingName, CancellationToken cancellationToken = default);
}

/// <summary>一项测试的登记：挂在哪个设置上、结论依赖哪些设置、由哪个探针执行。</summary>
/// <param name="SettingName">主设置：界面上测试按钮所在的那一项。</param>
/// <param name="DependsOn">结论依赖的其他设置；它们与主设置任一项被写入或清除，结果即过期。</param>
/// <param name="CheckType">探针类型，实现 <see cref="ISettingCheck"/>。</param>
public sealed record SettingCheckRegistration(string SettingName, IReadOnlyList<string> DependsOn, Type CheckType)
{
    /// <summary>主设置与依赖设置。</summary>
    public IEnumerable<string> Covers => DependsOn.Prepend(SettingName);
}
