using Leistd.Auditing;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.Auditing.Abstractions;
using Leistd.MultiTenancy.Abstractions;

namespace Leistd.MultiTenancy.EntityFrameworkCore.Entities;

/// <summary>
/// 表示宿主控制库中的租户连接记录。
/// </summary>
/// <remarks>
/// <b>改这一行就改了该租户的数据落在哪个库</b>，因此除并发令牌之外还带审计字段——必须能回答"谁在何时改的"。
/// 不实现 <see cref="ISoftDelete"/>：本记录与租户 1:1，随 <see cref="TenantRecord"/> 级联删除。
/// 写入统一通过 <see cref="ITenantConnectionConfigurationManager"/>，它负责一致性校验、版本递增与时间填充。
/// </remarks>
public class TenantConnectionRecord : ICreationAuditedObject, IModificationAuditedObject
{
    /// <summary>租户 Id，同时作为主键和 <see cref="TenantRecord"/> 外键。</summary>
    public Guid TenantId { get; set; }

    /// <summary>数据放置方式。</summary>
    public TenantDatabaseMode DatabaseMode { get; set; }

    /// <summary>运行时 DML Secret 引用。</summary>
    public string? RuntimeSecretReference { get; set; }

    /// <summary>迁移 DDL Secret 引用。</summary>
    public string? MigrationSecretReference { get; set; }

    /// <summary>配置版本。同时作为并发令牌，防止并发写入互相覆盖。</summary>
    public long Version { get; set; }

    /// <inheritdoc />
    public DateTime CreationTime { get; set; }

    /// <inheritdoc />
    public string? CreatorId { get; set; }

    /// <inheritdoc />
    public DateTime? LastModificationTime { get; set; }

    /// <inheritdoc />
    public string? LastModifierId { get; set; }
}
