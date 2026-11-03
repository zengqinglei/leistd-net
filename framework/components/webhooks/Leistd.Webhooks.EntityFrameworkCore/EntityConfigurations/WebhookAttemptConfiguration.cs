using Leistd.Webhooks.Delivery;
using Leistd.Webhooks.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Leistd.Webhooks.EntityFrameworkCore.EntityConfigurations;

/// <summary><see cref="WebhookAttempt"/> 的实体配置。</summary>
public class WebhookAttemptConfiguration : IEntityTypeConfiguration<WebhookAttempt>
{
    /// <summary><see cref="WebhookAttempt.Error"/> 的最大长度。</summary>
    public const int MaxErrorLength = 1024;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<WebhookAttempt> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Trigger).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.RequestHeaders).HasHeaderConversion();
        builder.Property(x => x.Response).HasMaxLength(WebhookSendResult.MaxResponseLength);
        builder.Property(x => x.Error).HasMaxLength(MaxErrorLength);

        builder.HasOne<WebhookDelivery>().WithMany().HasForeignKey(x => x.DeliveryId).OnDelete(DeleteBehavior.Cascade);

        // 投递详情按投递取全部尝试
        builder.HasIndex(x => x.DeliveryId);
    }
}
