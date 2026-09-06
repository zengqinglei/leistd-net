using Leistd.Notifications.Dtos;
using Leistd.Notifications.EntityFrameworkCore.Entities;
using Xunit;

namespace Leistd.Notifications.Tests;

// 落库时间必须来自发布方，不能挂在"宿主是否给这个 DbContext 挂了审计拦截器"上。
public class NotificationRecordMappingTests
{
    [Fact]
    public void FromDto_carries_the_creation_time_assigned_by_the_publisher()
    {
        var createdAt = new DateTime(2026, 9, 4, 10, 30, 0, DateTimeKind.Utc);

        var record = NotificationRecord.FromDto(
            new NotificationOutputDto { Title = "标题", CreationTime = createdAt },
            userId: "user-1");

        Assert.Equal(createdAt, record.CreationTime);
    }

    [Fact]
    public void FromDto_round_trips_through_ToDto()
    {
        var dto = new NotificationOutputDto
        {
            Title = "标题",
            Content = "内容",
            Link = "/orders/1",
            CreationTime = new DateTime(2026, 9, 4, 10, 30, 0, DateTimeKind.Utc),
            RelatedEntityId = "1",
            RelatedEntityType = "Order"
        };

        var round = NotificationRecord.FromDto(dto, userId: "user-1").ToDto();

        Assert.Equal(dto.Title, round.Title);
        Assert.Equal(dto.Content, round.Content);
        Assert.Equal(dto.Link, round.Link);
        Assert.Equal(dto.CreationTime, round.CreationTime);
        Assert.Equal(dto.RelatedEntityId, round.RelatedEntityId);
        Assert.Equal(dto.RelatedEntityType, round.RelatedEntityType);
    }
}
