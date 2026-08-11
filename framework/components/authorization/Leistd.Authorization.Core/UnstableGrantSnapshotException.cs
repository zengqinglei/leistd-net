namespace Leistd.Authorization;

/// <summary>
/// 在授予集合与版本持续变动的情况下取不到一致快照。
/// </summary>
/// <remarks>
/// 授予与版本是两次查询，中间夹进写入就会拼出"旧集合 + 新版本"。存储层用
/// "读版本 → 读数据 → 再读版本"确认期间无人写入，连续若干次都不稳定时抛出本异常。
///
/// 这是**读取失败**，不是保存冲突：此处没有调用方提交的期望版本，也没有"谁覆盖了谁"，
/// 因此不能复用 <see cref="PermissionGrantConcurrencyException"/>——那个异常的契约是
/// "旧页面保存撞车，提示重新加载并映射 HTTP 409"，语义对不上会让宿主把读取故障报成保存冲突。
/// 调用方直接重试即可。
/// </remarks>
public sealed class UnstableGrantSnapshotException(string subject, int attempts)
    : Exception($"Could not read a consistent grant snapshot for '{subject}' after {attempts} attempts.")
{
    /// <summary>读取的目标，形如 <c>Role/{id}</c> 或资源的 <c>{name}/{key}</c>，仅用于诊断。</summary>
    public string Subject { get; } = subject;

    /// <summary>已尝试次数。</summary>
    public int Attempts { get; } = attempts;
}
