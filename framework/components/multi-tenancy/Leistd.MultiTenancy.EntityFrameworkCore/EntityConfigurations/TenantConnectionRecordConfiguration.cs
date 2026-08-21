using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Leistd.MultiTenancy.EntityFrameworkCore;

/// <summary>租户连接记录的 EF Core 配置。</summary>
public class TenantConnectionRecordConfiguration : IEntityTypeConfiguration<TenantConnectionRecord>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<TenantConnectionRecord> builder)
    {
        builder.HasKey(x => x.TenantId);

        builder.Property(x => x.DatabaseMode).IsRequired();
        builder.Property(x => x.RuntimeSecretReference).HasMaxLength(512);
        builder.Property(x => x.MigrationSecretReference).HasMaxLength(512);
        builder.Property(x => x.Version).IsRequired().IsConcurrencyToken();

        builder.HasOne<TenantRecord>()
            .WithOne()
            .HasForeignKey<TenantConnectionRecord>(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.ToTable(table => table.HasCheckConstraint(
            "CK_TenantConnectionRecord_ModeSecrets",
            $"(\"{nameof(TenantConnectionRecord.DatabaseMode)}\" = {(int)TenantDatabaseMode.SharedDatabase} " +
            $"AND \"{nameof(TenantConnectionRecord.RuntimeSecretReference)}\" IS NULL " +
            $"AND \"{nameof(TenantConnectionRecord.MigrationSecretReference)}\" IS NULL) OR " +
            $"(\"{nameof(TenantConnectionRecord.DatabaseMode)}\" = {(int)TenantDatabaseMode.DedicatedDatabase} " +
            $"AND \"{nameof(TenantConnectionRecord.RuntimeSecretReference)}\" IS NOT NULL " +
            $"AND \"{nameof(TenantConnectionRecord.MigrationSecretReference)}\" IS NOT NULL)"));
    }
}
