using Leistd.Webhooks.Dtos;
using Leistd.Webhooks.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Leistd.Webhooks.EntityFrameworkCore.EntityConfigurations;

/// <summary><see cref="WebhookSubscription"/> 的实体配置。</summary>
public class WebhookSubscriptionConfiguration : IEntityTypeConfiguration<WebhookSubscription>
{
    /// <summary>密钥密文列的最大长度：Data Protection 密文约为明文的 2–3 倍，留足余量。</summary>
    public const int MaxProtectedSecretLength = 1024;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<WebhookSubscription> builder)
    {
        // 表名沿用 EF Core 默认约定，不加框架前缀
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(CreateWebhookSubscriptionInputDto.MaxNameLength).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(CreateWebhookSubscriptionInputDto.MaxDescriptionLength);
        builder.Property(x => x.Url).HasMaxLength(CreateWebhookSubscriptionInputDto.MaxUrlLength).IsRequired();

        // 基元集合按 EF 默认映射（PostgreSQL 为数组列，其余为 JSON 列）；发布时的 Contains 由 Provider 翻译
        builder.Property(x => x.EventTypes).IsRequired();
        builder.Property(x => x.Headers).HasHeaderConversion();

        builder.Property(x => x.Secret).HasMaxLength(MaxProtectedSecretLength).IsRequired();
        builder.Property(x => x.PreviousSecret).HasMaxLength(MaxProtectedSecretLength);

        // 存字符串而不是序号：便于直接查询，且枚举重排不改变历史含义
        builder.Property(x => x.DisabledReason).HasConversion<string>().HasMaxLength(32);

        builder.Property(x => x.CreatorId).HasMaxLength(64);
        builder.Property(x => x.LastModifierId).HasMaxLength(64);

        // 发布时按 (租户, 启用) 取候选订阅，再按事件类型筛
        builder.HasIndex(x => new { x.TenantId, x.IsEnabled });
    }
}
