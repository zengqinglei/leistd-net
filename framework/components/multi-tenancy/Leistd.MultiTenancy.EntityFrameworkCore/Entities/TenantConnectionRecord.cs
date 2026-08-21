namespace Leistd.MultiTenancy.EntityFrameworkCore;

/// <summary>
/// Identity Control DB 中的租户连接记录，不实现 <see cref="IMultiTenant"/>，不参与租户数据路由。
/// </summary>
public class TenantConnectionRecord
{
    /// <summary>租户 Id，同时作为主键和 <see cref="TenantRecord"/> 外键。</summary>
    public Guid TenantId { get; set; }

    /// <summary>数据放置方式。</summary>
    public TenantDatabaseMode DatabaseMode { get; set; }

    /// <summary>运行时 DML Secret 引用。</summary>
    public string? RuntimeSecretReference { get; set; }

    /// <summary>迁移 DDL Secret 引用。</summary>
    public string? MigrationSecretReference { get; set; }

    /// <summary>配置版本。</summary>
    public long Version { get; set; }
}
