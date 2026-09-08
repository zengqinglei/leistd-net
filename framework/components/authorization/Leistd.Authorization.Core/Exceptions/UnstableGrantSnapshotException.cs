using Leistd.ExceptionHandling;

namespace Leistd.Authorization.Exceptions;

/// <summary>
/// 表示无法读取稳定的权限授予快照。
/// </summary>
/// <remarks>
/// 这是读取失败而非保存冲突：映射为 503，调用方应直接重试，无需人工介入。
/// 存储层用"读版本 → 读数据 → 再读版本"确认期间无人写入，连续若干次不稳定时抛出。
/// </remarks>
public sealed class UnstableGrantSnapshotException(string subject, int attempts)
    : ServiceUnavailableException(
        $"Could not read a consistent grant snapshot for '{subject}' after {attempts} attempts.")
{
    /// <summary>读取的目标，形如 <c>Role/{id}</c> 或资源的 <c>{name}/{key}</c>，仅用于诊断。</summary>
    public string Subject { get; } = subject;

    /// <summary>已尝试次数。</summary>
    public int Attempts { get; } = attempts;
}
