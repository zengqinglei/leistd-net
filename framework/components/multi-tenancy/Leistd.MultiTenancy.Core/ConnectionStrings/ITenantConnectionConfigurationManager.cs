using Leistd.MultiTenancy.Exceptions;

namespace Leistd.MultiTenancy.ConnectionStrings;

/// <summary>
/// 租户连接配置的唯一写入口，负责模式、Secret 引用与版本不变量。
/// </summary>
public interface ITenantConnectionConfigurationManager
{
    /// <summary>
    /// 创建或更新租户连接配置。共享模式不接受 Secret 引用；独立模式要求两个引用均非空。
    /// </summary>
    /// <param name="tenantId">目标租户</param>
    /// <param name="databaseMode">数据放置方式</param>
    /// <param name="runtimeSecretReference">运行时 DML Secret 引用</param>
    /// <param name="migrationSecretReference">迁移 DDL Secret 引用</param>
    /// <param name="expectedVersion">
    /// 调用方读到的版本；<see langword="null"/> 表示预期配置尚不存在，不表示跳过并发校验。
    /// </param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <exception cref="TenantNotFoundException">租户不存在或已删除。</exception>
    /// <exception cref="ArgumentException">模式与 Secret 引用不匹配。</exception>
    /// <exception cref="TenantConnectionVersionConflictException">
    /// <paramref name="expectedVersion"/> 与实际不符，含"预期存在却不存在"与"预期不存在却已存在"两种。
    /// </exception>
    Task<TenantConnectionConfiguration> SetAsync(
        Guid tenantId,
        TenantDatabaseMode databaseMode,
        string? runtimeSecretReference,
        string? migrationSecretReference,
        long? expectedVersion,
        CancellationToken cancellationToken = default);
}
