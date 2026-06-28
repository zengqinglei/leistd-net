using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Leistd.Notifications.EntityFrameworkCore;

/// <summary>
/// NotificationRecord EF Core 实体配置。
/// </summary>
public class NotificationRecordConfiguration : IEntityTypeConfiguration<NotificationRecord>
{
    public void Configure(EntityTypeBuilder<NotificationRecord> builder)
    {
        builder.ToTable("LeistdNotifications");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.UserId)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.Title)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.Content)
            .HasMaxLength(2000);

        builder.Property(x => x.Type)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.Link)
            .HasMaxLength(512);

        builder.Property(x => x.Icon)
            .HasMaxLength(64);

        builder.Property(x => x.RelatedEntityId)
            .HasMaxLength(128);

        builder.Property(x => x.RelatedEntityType)
            .HasMaxLength(64);

        builder.Property(x => x.CreatorId)
            .HasMaxLength(64);

        // 按用户 + 创建时间查询（最频繁：拉取用户通知列表）
        builder.HasIndex(x => new { x.UserId, x.CreationTime })
            .HasDatabaseName("IX_LeistdNotifications_UserId_CreationTime");

        // 按用户 + 已读状态查询（未读数）
        builder.HasIndex(x => new { x.UserId, x.IsRead })
            .HasDatabaseName("IX_LeistdNotifications_UserId_IsRead");
    }
}
