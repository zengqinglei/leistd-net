using Leistd.Ddd.Application.Contracts.Dtos;
using Leistd.ObjectMapping.Extensions;
using Leistd.ObjectMapping;
using Leistd.ObjectMapping.Abstractions;

namespace Leistd.Ddd.Application.Extensions;

/// <summary>
/// 提供应用层分页对象映射扩展。
/// </summary>
public static class ObjectMapperExtensions
{
    /// <summary>
    /// 映射分页结果中的项目并保留总数。
    /// </summary>
    public static PagedResultDto<TDestination> MapPagedResult<TSource, TDestination>(
        this IObjectMapper mapper,
        PagedResultDto<TSource> pagedSource)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(pagedSource);

        var mappedItems = mapper.MapList<TSource, TDestination>(pagedSource.Items);
        return new PagedResultDto<TDestination>(pagedSource.TotalCount, mappedItems);
    }
}
