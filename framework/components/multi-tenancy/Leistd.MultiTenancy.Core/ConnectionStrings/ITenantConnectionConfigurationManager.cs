namespace Leistd.MultiTenancy;

/// <summary>
/// 租户连接配置的唯一写入口，负责模式、Secret 引用与版本不变量。
/// </summary>
public interface ITenantConnectionConfigurationManager
{
    /// <summary>
    /// 创建或更新租户连接配置。共享模式不接受 Secret 引用；独立模式要求两个引用均非空。
    /// </summary>
    /// <exception cref="TenantNotFoundException">租户不存在或已删除。</exception>
    /// <exception cref="ArgumentException">模式与 Secret 引用不匹配。</exception>
    Task<TenantConnectionConfiguration> SetAsync(
        Guid tenantId,
        TenantDatabaseMode databaseMode,
        string? runtimeSecretReference,
        string? migrationSecretReference,
        CancellationToken cancellationToken = default);
}
