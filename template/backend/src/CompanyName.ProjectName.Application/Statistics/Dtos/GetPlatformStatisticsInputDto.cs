namespace CompanyName.ProjectName.Application.Statistics.Dtos;

/// <summary>获取平台统计输入 DTO：一段日历区间，以及解释这段日期的时区（规则见 <see cref="StatisticsRangeInputDto"/>）。</summary>
public record GetPlatformStatisticsInputDto : StatisticsRangeInputDto;
