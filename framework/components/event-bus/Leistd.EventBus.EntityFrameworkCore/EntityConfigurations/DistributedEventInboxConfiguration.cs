using Leistd.EventBus.EntityFrameworkCore.Entities;
using Leistd.Tracing.Constants;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

namespace Leistd.EventBus.EntityFrameworkCore.EntityConfigurations;

/// <summary><see cref="DistributedEventInbox"/> 的实体配置。</summary>
public sealed class DistributedEventInboxConfiguration : IEntityTypeConfiguration<DistributedEventInbox>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<DistributedEventInbox> builder)
    {
        builder.Property(x => x.OccurredAt).HasConversion(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
            value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc)));
        builder.Property(x => x.ReceivedAt).HasConversion(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
            value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc)));
        builder.Property(x => x.LastModificationTime).HasConversion(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
            value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc)));
        builder.Property(x => x.NextAttemptAt).HasConversion(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
            value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc)));
        builder.Property(x => x.ProcessedAt).HasConversion(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
            value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc)));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.EventName).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Headers).HasMessageDictionaryConversion();
        builder.Property(x => x.TraceId).HasMaxLength(32);
        builder.HasIndex(x => x.TraceId);
        builder.Property(x => x.CorrelationId).HasMaxLength(CorrelationIdConstants.MaxLength);
        builder.HasIndex(x => x.CorrelationId);
        builder.Property(x => x.OrderingKey).HasMaxLength(256);
        builder.Property(x => x.Payload).IsRequired();
        builder.Property(x => x.LastErrorCode).HasMaxLength(256);
        builder.Property(x => x.LastError).HasMaxLength(512);
        builder.HasIndex(x => new { x.Status, x.NextAttemptAt, x.Id });
        builder.HasIndex(x => new { x.TenantId, x.EventName, x.OrderingKey, x.Id });
        builder.HasIndex(x => new { x.EventName, x.Status });
        builder.HasIndex(x => new { x.TenantId, x.EventId, x.Consumer }).IsUnique().HasFilter("\"TenantId\" IS NOT NULL");
        builder.HasIndex(x => new { x.EventId, x.Consumer }).IsUnique().HasFilter("\"TenantId\" IS NULL");
        builder.Property(x => x.Consumer).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Coordinates).HasMessageDictionaryConversion();
        builder.Property(x => x.AttemptCount).IsConcurrencyToken();
        builder.HasIndex(x => new { x.Status, x.LastModificationTime });
    }
}
