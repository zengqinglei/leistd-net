using Leistd.Webhooks.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Leistd.Webhooks.EntityFrameworkCore.EntityConfigurations;

/// <summary><see cref="WebhookDelivery"/> 的实体配置。</summary>
public class WebhookDeliveryConfiguration : IEntityTypeConfiguration<WebhookDelivery>
{
    /// <summary><see cref="WebhookDelivery.LastError"/> 的最大长度。</summary>
    public const int MaxLastErrorLength = 1024;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<WebhookDelivery> builder)
    {
        builder.HasKey(x => x.Id);

        // 存字符串而不是序号：便于直接查询，且枚举重排不改变历史含义
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.NextTrigger).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.LastError).HasMaxLength(MaxLastErrorLength);

        // 删除订阅或事件时投递一并删除；组件自己删除时仍逐条加载后删除，不依赖级联
        builder.HasOne<WebhookSubscription>().WithMany().HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<WebhookEvent>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);

        // 兜底扫描整库取到期的待投递记录（IgnoreQueryFilters，不带 TenantId）
        builder.HasIndex(x => new { x.Status, x.NextAttemptAt });

        // 按订阅查投递、订阅的最近一次投递与近 7 天成功率
        builder.HasIndex(x => new { x.SubscriptionId, x.CreationTime });

        // 统计按创建时间分桶；保留期整库按创建时间扫
        builder.HasIndex(x => x.CreationTime);
    }
}
