using System.Collections.ObjectModel;

namespace Leistd.Ddd.Application.Contracts.Dtos;

/// <summary>
/// 分页结果。
/// </summary>
/// <typeparam name="T">条目类型。</typeparam>
/// <param name="TotalCount">符合条件的总条数，与当前页条数无关。</param>
/// <param name="Items">当前页条目。</param>
public record PagedResultDto<T>(long TotalCount, IReadOnlyList<T> Items)
{
    /// <summary>从任意序列构造分页结果；<paramref name="items"/> 为 <see langword="null"/> 时得到空集合。</summary>
    /// <param name="totalCount">符合条件的总条数。</param>
    /// <param name="items">当前页条目。</param>
    public PagedResultDto(long totalCount, IEnumerable<T> items)
        : this(totalCount, items?.ToList().AsReadOnly() ?? ReadOnlyCollection<T>.Empty)
    {
    }
}
