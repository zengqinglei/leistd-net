using Leistd.EventBus.EntityFrameworkCore.Entities;
using Leistd.Tracing.Constants;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

namespace Leistd.EventBus.EntityFrameworkCore.EntityConfigurations;

/// <summary><see cref="DistributedEventRejection"/> 的实体配置。</summary>
public sealed class DistributedEventRejectionConfiguration : IEntityTypeConfiguration<DistributedEventRejection>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<DistributedEventRejection> builder)
    {
        builder.Property(x => x.ReceivedAt).HasConversion(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
            value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc)));
        builder.Property(x => x.LastModificationTime).HasConversion(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
            value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc)));
        builder.Property(x => x.ResolvedAt).HasConversion(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
            value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc)));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.EventName).HasMaxLength(256);
        builder.Property(x => x.Headers).HasMessageDictionaryConversion();
        builder.Property(x => x.TraceId).HasMaxLength(32);
        builder.HasIndex(x => x.TraceId);
        builder.Property(x => x.CorrelationId).HasMaxLength(CorrelationIdConstants.MaxLength);
        builder.HasIndex(x => x.CorrelationId);
        builder.Property(x => x.Transport).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Error).HasMaxLength(512).IsRequired();
        builder.Property(x => x.RawBody).IsRequired();
        builder.Property(x => x.Coordinates).HasMessageDictionaryConversion();
        builder.HasIndex(x => new { x.Status, x.LastModificationTime });
    }
}
