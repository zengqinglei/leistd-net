using System.ComponentModel.DataAnnotations;

namespace Leistd.Data.Paging;

/// <summary>
/// 分页查询请求：跳过条数、返回条数与排序表达式。
/// </summary>
/// <remarks>
/// <para>存储、用例与端点共用这一个类型，参数名因此在全框架只有 <c>offset</c> / <c>limit</c> / <c>sorting</c> 一套。
/// MVC 的 <c>[FromQuery]</c> 可直接绑定它（含派生类型）；Minimal API 的 <c>[AsParameters]</c> 会把
/// 没有默认值的非空属性当成必填参数（属性初始化器不算默认值），缺 <c>offset</c> 的请求因此直接 400——
/// Minimal API 端点应声明带默认值的 <c>offset</c> / <c>limit</c> / <c>sorting</c> 参数，再构造本类型。</para>
/// <para>数值边界以 <see cref="RangeAttribute"/> 表达，越界产生验证错误。<see cref="MaximumLimit"/> 是影响面封顶，
/// 确需更大值的接口应自定义入参类型。<see cref="Sorting"/> 不在此约束：可排序字段由各查询按白名单解析，
/// 不得原样拼进查询。</para>
/// </remarks>
public record PageRequest
{
    /// <summary>未指定 <see cref="Limit"/> 时的默认返回条数。</summary>
    public const int DefaultLimit = 10;

    /// <summary>单次请求允许的最大返回条数。</summary>
    public const int MaximumLimit = 1000;

    /// <summary>跳过的条数，从 0 开始。负值产生验证错误。</summary>
    [Range(0, int.MaxValue, ErrorMessage = "{0} must be 0 or greater.")]
    public int Offset { get; init; }

    /// <summary>返回条数，取值 1–<see cref="MaximumLimit"/>，默认 <see cref="DefaultLimit"/>。</summary>
    [Range(1, MaximumLimit, ErrorMessage = "{0} must be between {1} and {2}.")]
    public int Limit { get; init; } = DefaultLimit;

    /// <summary>排序表达式，由各查询自行解析与校验；<see langword="null"/> 表示用该查询的默认排序。</summary>
    public string? Sorting { get; init; }
}
