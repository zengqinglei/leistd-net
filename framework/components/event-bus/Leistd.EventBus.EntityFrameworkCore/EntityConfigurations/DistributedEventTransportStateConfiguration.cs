using Leistd.EventBus.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Leistd.EventBus.EntityFrameworkCore.EntityConfigurations;

/// <summary><see cref="DistributedEventTransportState"/> 的宿主库映射。</summary>
public sealed class DistributedEventTransportStateConfiguration : IEntityTypeConfiguration<DistributedEventTransportState>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<DistributedEventTransportState> builder)
    {
        builder.HasKey(x => x.Instance);
        builder.Property(x => x.Instance).HasMaxLength(512);
        builder.Property(x => x.TransportName).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.StatusChangedAt).HasConversion(new ValueConverter<DateTime, DateTime>(
            value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc)));
        builder.Property(x => x.ReportedAt).HasConversion(new ValueConverter<DateTime, DateTime>(
            value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc)));
        builder.HasIndex(x => x.ReportedAt);
    }
}
