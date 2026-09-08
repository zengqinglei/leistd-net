namespace Leistd.Authorization.Abstractions;

/// <summary>
/// 检查当前用户的权限授予状态。
/// </summary>
/// <example>
/// <code>
/// if (!await permissionChecker.IsGrantedAsync("Orders.Update", ct))
///     throw new ForbiddenException();
///
/// // 批量检查：一次读取，其后都是内存查找
/// var result = await permissionChecker.IsGrantedAsync(["Orders.Read", "Orders.Update"], ct);
/// if (!result.AllGranted) { /* 入参为空时同样不通过 */ }
/// </code>
/// </example>
public interface IPermissionChecker
{
    /// <summary>
    /// 检查当前用户是否拥有指定权限。
    /// </summary>
    /// <param name="name">权限名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>如果拥有权限则返回 true</returns>
    Task<bool> IsGrantedAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查当前用户是否拥有指定的多个权限。
    /// </summary>
    /// <param name="names">权限名称数组</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>多权限检查结果</returns>
    Task<MultiplePermissionGrantResult> IsGrantedAsync(
        string[] names,
        CancellationToken cancellationToken = default);

}
/// <summary>
/// 表示多个权限的检查结果。
/// </summary>
/// <param name="Results">权限检查结果字典（权限名 -> 是否授予）</param>
public readonly record struct MultiplePermissionGrantResult(
    IReadOnlyDictionary<string, bool> Results)
{
    /// <summary>
    /// 获取是否所有权限都已授予；空集合返回 <see langword="false"/>。
    /// </summary>
    /// <remarks>
    /// 没有任何权限被检查即视为不通过（与 <see cref="Enumerable.All{TSource}"/> 的空集恒真相反），
    /// 因此入参为空时不会放行。需要区分"空集合"与"检查后未授予"请自行判断 <see cref="Results"/> 的元素数。
    /// </remarks>
    public bool AllGranted => Results.Count > 0 && Results.Values.All(x => x);

    /// <summary>
    /// 是否至少有一个权限已授予（空集合返回 <see langword="false"/>，与 <c>Enumerable.Any</c> 语义一致）
    /// </summary>
    public bool AnyGranted => Results.Values.Any(x => x);
}
