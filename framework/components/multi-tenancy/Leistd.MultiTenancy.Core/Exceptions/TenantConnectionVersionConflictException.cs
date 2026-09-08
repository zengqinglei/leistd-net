using Leistd.ExceptionHandling;

namespace Leistd.MultiTenancy.Exceptions;

/// <summary>
/// 表示租户连接配置版本不匹配。
/// </summary>
/// <remarks>
/// 写入必须携带预期版本；冲突时由调用方重新读取并决定如何合并。
/// </remarks>
/// <param name="tenantId">目标租户</param>
/// <param name="expectedVersion">调用方预期的版本；<see langword="null"/> 表示调用方预期该配置尚不存在</param>
/// <param name="actualVersion">实际版本；<see langword="null"/> 表示配置实际不存在</param>
/// <param name="innerException">
/// 底层并发异常。预检失败时为 <see langword="null"/>；真正同时提交、由数据库并发令牌拦下时，
/// 这里带上 <c>DbUpdateConcurrencyException</c> 以保留现场
/// </param>
public class TenantConnectionVersionConflictException(
    Guid tenantId,
    long? expectedVersion,
    long? actualVersion,
    Exception? innerException = null)
    : ConflictException(
        $"Tenant '{tenantId}' connection configuration version mismatch: " +
        $"expected {Describe(expectedVersion)} but found {Describe(actualVersion)}. " +
        "Re-read the configuration and retry.",
        innerException)
{
    /// <summary>获取目标租户标识。</summary>
    public Guid TenantId { get; } = tenantId;

    /// <summary>获取预期版本；<see langword="null"/> 表示预期配置不存在。</summary>
    public long? ExpectedVersion { get; } = expectedVersion;

    /// <summary>获取实际版本；<see langword="null"/> 表示配置不存在。</summary>
    public long? ActualVersion { get; } = actualVersion;

    private static string Describe(long? version) =>
        version is { } value ? $"version {value}" : "no configuration";
}
