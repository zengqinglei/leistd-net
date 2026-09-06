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
    /// 调用方读到的版本，<see langword="null"/> 表示调用方预期该配置<b>尚不存在</b>（首次创建）。
    /// <b>没有"不检查"这个取值</b>：这一行决定租户数据落在哪个库，丢更新是静默的，
    /// 留一个跳过检查的入口就等于留一条默认后写者胜出的路径
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
