using Leistd.Webhooks.Dtos;
using Leistd.Webhooks.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Leistd.Webhooks.EntityFrameworkCore.EntityConfigurations;

/// <summary><see cref="WebhookEvent"/> 的实体配置。</summary>
public class WebhookEventConfiguration : IEntityTypeConfiguration<WebhookEvent>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<WebhookEvent> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Type).HasMaxLength(CreateWebhookSubscriptionInputDto.MaxEventTypeLength).IsRequired();
        // 载荷不设长度上限：截断的 JSON 既不能解析也验不过签
        builder.Property(x => x.Payload).IsRequired();

        // 保留期整库按时间扫没有投递的事件
        builder.HasIndex(x => x.OccurredAt);
    }
}
