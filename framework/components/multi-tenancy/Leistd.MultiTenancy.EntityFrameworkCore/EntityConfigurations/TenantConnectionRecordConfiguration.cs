using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Leistd.MultiTenancy.EntityFrameworkCore.Entities;
using Leistd.MultiTenancy.ConnectionStrings;

namespace Leistd.MultiTenancy.EntityFrameworkCore.EntityConfigurations;

/// <summary>租户连接记录的 EF Core 配置。</summary>
public class TenantConnectionRecordConfiguration : IEntityTypeConfiguration<TenantConnectionRecord>
{
    /// <summary>获取模式与 Secret 引用的一致性约束名称。</summary>
    public const string ModeSecretsCheckConstraintName = "CK_TenantConnectionRecord_ModeSecrets";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<TenantConnectionRecord> builder)
    {
        builder.HasKey(x => x.TenantId);

        builder.Property(x => x.DatabaseMode).IsRequired();

        // Secret 引用不进索引也不进唯一约束：它是配置值不是标识符，
        // 且是本表唯一的敏感字段——进索引等于在更多地方留副本
        builder.Property(x => x.RuntimeSecretReference).HasMaxLength(512);
        builder.Property(x => x.MigrationSecretReference).HasMaxLength(512);

        builder.Property(x => x.Version).IsRequired().IsConcurrencyToken();

        builder.Property(x => x.CreatorId).HasMaxLength(64);
        builder.Property(x => x.LastModifierId).HasMaxLength(64);

        builder.HasOne<TenantRecord>()
            .WithOne()
            .HasForeignKey<TenantConnectionRecord>(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.ToTable(table => table.HasCheckConstraint(
            ModeSecretsCheckConstraintName,
            $"({Column(nameof(TenantConnectionRecord.DatabaseMode))} = {(int)TenantDatabaseMode.SharedDatabase} " +
            $"AND {Column(nameof(TenantConnectionRecord.RuntimeSecretReference))} IS NULL " +
            $"AND {Column(nameof(TenantConnectionRecord.MigrationSecretReference))} IS NULL) OR " +
            $"({Column(nameof(TenantConnectionRecord.DatabaseMode))} = {(int)TenantDatabaseMode.DedicatedDatabase} " +
            $"AND {Column(nameof(TenantConnectionRecord.RuntimeSecretReference))} IS NOT NULL " +
            $"AND {Column(nameof(TenantConnectionRecord.MigrationSecretReference))} IS NOT NULL)"));
    }

    // 约束使用属性名作为列名，双引号适用于 PostgreSQL 与 SQLite。
    // 采用列名约定时，宿主须按实际列名重建约束；配置阶段约定尚未执行。
    private static string Column(string propertyName) => $"\"{propertyName}\"";
}
