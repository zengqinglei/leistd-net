using System.ComponentModel.DataAnnotations;
using CompanyName.ProjectName.Application.Settings.Timing;
using CompanyName.ProjectName.Application.Statistics.Dtos;

namespace CompanyName.ProjectName.Application.Statistics;

/// <summary>一次按日历区间的统计：请求的日期与时区、换算出的 UTC 范围与上一区间，以及本期的桶边界。</summary>
/// <remarks>
/// <para>各统计用例共用同一套换算，区间、桶与上一区间的口径见生成项目的批量与统计接口规范。</para>
/// <para><see cref="Edges"/> 是本期的桶边界（首尾即本期起止），零长度区间为空。</para>
/// </remarks>
public sealed record StatisticsPeriod(
    DateOnly StartDate,
    DateOnly EndDate,
    TimeZoneInfo TimeZone,
    StatisticsGranularity Granularity,
    DateTime Start,
    DateTime End,
    DateTime PreviousStart,
    DateTime PreviousEnd,
    DateTime[] Edges)
{
    /// <summary>各桶起点（UTC，升序），与按桶序列等长；零长度区间为空。</summary>
    public IReadOnlyList<DateTime> Buckets => Edges.Length == 0 ? [] : Edges[..^1];

    /// <summary>回显给调用方的区间。</summary>
    public StatisticsRangeOutputDto ToRange() => new()
    {
        StartDate = StartDate,
        EndDate = EndDate,
        TimeZone = TimeZone.Id,
        Granularity = Granularity,
        Start = Start,
        End = End,
        PreviousStart = PreviousStart,
        PreviousEnd = PreviousEnd
    };

    /// <summary>按请求的时区把日历区间换算成 UTC 范围、上一区间与桶边界。</summary>
    /// <remarks>
    /// <para>区间含今天时截到 <paramref name="now"/>；上一区间按日历前移同样的天数，比较相同的本地进度。
    /// 夏令时当天的日桶是 23 或 25 小时。</para>
    /// <para>时区可用与结束日不晚于今天要用时区与时钟判断，DTO 自身校验不了；与其他字段错误同一通道（400）。</para>
    /// </remarks>
    /// <param name="input">已通过 DTO 校验的区间。</param>
    /// <param name="timeZones">时区解析。</param>
    /// <param name="now">此刻（UTC），调用方只读一次时钟。</param>
    /// <exception cref="ValidationException">时区不可用，或结束日晚于该时区的今天。</exception>
    public static StatisticsPeriod Resolve(StatisticsRangeInputDto input, IUserTimeZoneProvider timeZones, DateTime now)
    {
        if (!timeZones.TryResolve(input.TimeZone, out var timeZone))
        {
            throw InvalidField("The time zone is not a valid IANA time zone.", nameof(StatisticsRangeInputDto.TimeZone));
        }

        var startDate = input.StartDate!.Value;
        var endDate = input.EndDate!.Value;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(now, timeZone));
        if (endDate > today)
        {
            throw InvalidField("The end date cannot be later than today.", nameof(StatisticsRangeInputDto.EndDate));
        }

        var days = endDate.DayNumber - startDate.DayNumber + 1;
        var start = DayStart(startDate, timeZone);
        var fullEnd = DayStart(endDate.AddDays(1), timeZone);
        var end = fullEnd < now ? fullEnd : now;

        // 上一区间比较相同的本地进度：区间含今天时，终点是此刻的本地钟点前移同样的天数，而不是减 N×24 小时
        var previousStart = DayStart(startDate.AddDays(-days), timeZone);
        var previousEnd = end == fullEnd
            ? DayStart(endDate.AddDays(1 - days), timeZone)
            : ToUtc(TimeZoneInfo.ConvertTimeFromUtc(now, timeZone).AddDays(-days), timeZone);

        var granularity = days == 1 ? StatisticsGranularity.Hour : StatisticsGranularity.Day;
        var edges = new List<DateTime>();
        if (end > start)
        {
            if (granularity == StatisticsGranularity.Hour)
            {
                // 小时桶按真实经过的时间步进：回拨日有 25 个、两个本地 01 点各成一桶，半小时时区的桶起点落在本地 :30
                for (var edge = start; edge < end; edge = edge.AddHours(1))
                {
                    edges.Add(edge);
                }
            }
            else
            {
                for (var date = startDate; DayStart(date, timeZone) is var edge && edge < end; date = date.AddDays(1))
                {
                    edges.Add(edge);
                }
            }

            edges.Add(end);
        }

        return new StatisticsPeriod(
            startDate, endDate, timeZone, granularity, start, end, previousStart, previousEnd, [.. edges]);
    }

    // 某个本地日的第一个时刻
    private static DateTime DayStart(DateOnly date, TimeZoneInfo timeZone)
        => ToUtc(date.ToDateTime(TimeOnly.MinValue), timeZone);

    // 本地时刻换算 UTC：落在跳时缺口里的取缺口之后的第一个时刻，回拨重复的取较早的那一个
    private static DateTime ToUtc(DateTime local, TimeZoneInfo timeZone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (timeZone.IsInvalidTime(local))
        {
            // 跳时都发生在整分钟上，从所在的那一分钟往后找到第一个存在的本地时刻
            local = new DateTime(local.Year, local.Month, local.Day, local.Hour, local.Minute, 0, DateTimeKind.Unspecified);
            while (timeZone.IsInvalidTime(local))
            {
                local = local.AddMinutes(1);
            }
        }

        if (timeZone.IsAmbiguousTime(local))
        {
            var earlierOffset = timeZone.GetAmbiguousTimeOffsets(local).Max();
            return DateTime.SpecifyKind(local - earlierOffset, DateTimeKind.Utc);
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, timeZone);
    }

    private static ValidationException InvalidField(string message, string member)
        => new(new ValidationResult(message, [member]), validatingAttribute: null, value: null);
}
