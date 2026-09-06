using System.Linq.Expressions;
using Leistd.ExceptionHandling;

namespace CompanyName.ProjectName.Application.Shared.Paging;

/// <summary>
/// 把 <c>"字段 asc|desc"</c> 排序串解析成字段名与方向
/// </summary>
/// <remarks>
/// 本类只解析字段与方向；各应用服务用显式白名单把 API 字段映射为强类型表达式。
/// 未公开的实体属性不能通过排序字符串访问，无效字段或方向统一返回 400。
/// </remarks>
internal static class SortingRequest
{
    /// <summary>
    /// 解析排序串；为空时返回默认字段与升序
    /// </summary>
    /// <param name="sorting">形如 <c>"username desc"</c>；方向省略时按升序</param>
    /// <param name="defaultField">入参为空时使用的字段</param>
    internal static (string Field, bool Descending) Parse(string? sorting, string defaultField)
    {
        if (string.IsNullOrWhiteSpace(sorting))
        {
            return (defaultField, false);
        }

        var parts = sorting.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var field = parts[0];

        var descending = parts.Length switch
        {
            1 => false,
            2 when parts[1].Equals("asc", StringComparison.OrdinalIgnoreCase) => false,
            2 when parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase) => true,
            _ => throw Invalid(sorting)
        };

        return (field, descending);
    }

    /// <summary>
    /// 按方向应用一个强类型排序键
    /// </summary>
    internal static IOrderedQueryable<T> By<T, TKey>(
        IQueryable<T> query, Expression<Func<T, TKey>> key, bool descending) =>
        descending ? query.OrderByDescending(key) : query.OrderBy(key);

    /// <inheritdoc cref="By{T, TKey}(IQueryable{T}, Expression{Func{T, TKey}}, bool)"/>
    internal static IOrderedEnumerable<T> By<T, TKey>(
        IEnumerable<T> source, Func<T, TKey> key, bool descending) =>
        descending ? source.OrderByDescending(key) : source.OrderBy(key);

    /// <summary>
    /// 字段不在白名单内：400 并点名该字段
    /// </summary>
    /// <remarks>
    /// 返回类型是基类 <see cref="BusinessException"/> 而非 <see cref="BadRequestException"/>：
    /// <c>WithCode</c> 返回基类，写成派生类时只有不带链式调用的形态编译得过。
    /// 状态码由实际构造的类型决定，不受声明类型影响。
    /// </remarks>
    internal static BusinessException UnknownField(string field) =>
        new BadRequestException($"Unsupported sorting field: {field}")
#if (IncludeLocalization)
            .WithCode("Paging:SortingFieldUnsupported")
            .WithData("Field", field)
#endif
            ;

    private static BusinessException Invalid(string sorting) =>
        new BadRequestException($"Invalid sorting expression: {sorting}")
#if (IncludeLocalization)
            .WithCode("Paging:SortingInvalid")
            .WithData("Sorting", sorting)
#endif
            ;
}
