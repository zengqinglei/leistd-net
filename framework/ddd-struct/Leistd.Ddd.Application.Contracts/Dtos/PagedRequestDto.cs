using System.ComponentModel.DataAnnotations;

namespace Leistd.Ddd.Application.Contracts.Dtos;

/// <summary>
/// 表示分页查询请求。
/// </summary>
/// <remarks>
/// 数值边界以 <see cref="RangeAttribute"/> 表达，越界产生验证错误（<c>[ApiController]</c> 默认返回 400）。
/// <see cref="MaximumLimit"/> 是影响面封顶，确需更大值的接口应自定义入参类型。
/// <see cref="Sorting"/> 不在此约束：可排序字段由各应用服务自行校验。
/// </remarks>
public record PagedRequestDto
{
    /// <summary>未指定 <see cref="Limit"/> 时的默认返回条数。</summary>
    protected const int DefaultLimit = 10;

    /// <summary>获取单次请求允许的最大返回条数。</summary>
    public const int MaximumLimit = 1000;

    /// <summary>跳过的条数，从 0 开始。负值产生验证错误。</summary>
    [Range(0, int.MaxValue, ErrorMessage = "{0} must be 0 or greater.")]
    public int Offset { get; init; }

    /// <summary>返回条数，取值 1–<see cref="MaximumLimit"/>，默认 <see cref="DefaultLimit"/>。</summary>
    [Range(1, MaximumLimit, ErrorMessage = "{0} must be between {1} and {2}.")]
    public int Limit { get; init; } = DefaultLimit;

    /// <summary>排序表达式，由各应用服务自行解析与校验；<see langword="null"/> 表示用该服务的默认排序。</summary>
    public string? Sorting { get; init; }
}
