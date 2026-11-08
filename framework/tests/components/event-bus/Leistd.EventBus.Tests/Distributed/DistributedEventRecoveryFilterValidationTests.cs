using System.ComponentModel.DataAnnotations;
using Leistd.EventBus.Distributed.Dtos;
using Leistd.EventBus.Distributed.Stores;
using Xunit;

namespace Leistd.EventBus.Tests.Distributed;

public sealed class DistributedEventRecoveryFilterValidationTests
{
    /// <summary>按当前筛选恢复沿用列表的字段边界，避免超长筛选绕过列表入口的校验。</summary>
    [Theory]
    [InlineData("outbox", "EventName")]
    [InlineData("outbox", "Keyword")]
    [InlineData("outbox", "ErrorCode")]
    [InlineData("inbox", "EventName")]
    [InlineData("inbox", "Keyword")]
    [InlineData("inbox", "ErrorCode")]
    [InlineData("inbox", "Consumer")]
    [InlineData("rejections", "EventName")]
    [InlineData("rejections", "Keyword")]
    [InlineData("rejections", "Reason")]
    public void Recovery_filter_accepts_the_limit_and_rejects_overlong_fields(string kind, string field)
    {
        var before = new DateTime(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc);
        object input = kind switch
        {
            "outbox" => new DistributedEventOutboxFilterDto { Before = before },
            "inbox" => new DistributedEventInboxFilterDto { Before = before, Status = DistributedEventInboxStatus.Failed },
            _ => new DistributedEventRejectionFilterDto { Before = before }
        };
        var property = input.GetType().GetProperty(field)!;
        property.SetValue(input, new string('x', 256));
        Assert.True(Validator.TryValidateObject(input, new ValidationContext(input), [], true));

        property.SetValue(input, new string('x', 257));
        var results = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(input, new ValidationContext(input), results, true));
        Assert.Contains(results, result => result.MemberNames.Contains(field));
    }
}
