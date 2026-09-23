using Leistd.MultiTenancy;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.MultiTenancy.Context;
using Leistd.MultiTenancy.Errors;
using Leistd.MultiTenancy.Management;
using Leistd.MultiTenancy.Tenancy;

namespace Leistd.Authorization.Definitions;

/// <summary>
/// 将权限定义添加到定义上下文。
/// </summary>
/// <example>
/// <code>
/// public class OrderPermissionDefinitionProvider : IPermissionDefinitionProvider
/// {
///     public void Define(IPermissionDefinitionContext context)
///     {
///         var group = context.GetOrAddGroup("Orders");
///         var orders = group.AddPermission("Orders.Read", MultiTenancySides.Both);
///         orders.AddChild("Orders.Update");   // 授予子权限时祖先自动补齐
///     }
/// }
/// </code>
/// </example>
public interface IPermissionDefinitionProvider
{
    /// <summary>
    /// 定义权限。
    /// </summary>
    /// <param name="context">权限定义上下文</param>
    void Define(IPermissionDefinitionContext context);
}

/// <summary>
/// 提供权限定义的注册和查询上下文。
/// </summary>
/// <remarks>
/// 每个权限必须归属于一个权限组，游离权限无法被管理界面表达。
/// 权限名在所有组与层级中全局唯一，重复注册在启动期抛出异常。
/// </remarks>
public interface IPermissionDefinitionContext
{
    /// <summary>
    /// 获取或创建权限组。
    /// </summary>
    /// <param name="name">组名称</param>
    /// <param name="displayName">默认显示名（通常是英文），未启用本地化或缺词条时直接展示；词条键按约定由名称拼出。</param>
    /// <returns>权限组</returns>
    /// <remarks>组不带侧别：侧别由每个权限在 <c>AddPermission</c> 处显式声明，见那里的说明。</remarks>
    IPermissionGroupDefinition GetOrAddGroup(string name, string? displayName = null);

    /// <summary>
    /// 获取权限定义。
    /// </summary>
    /// <param name="name">权限名称</param>
    /// <returns>权限定义，如果不存在则返回 null</returns>
    IPermissionDefinition? GetPermissionOrNull(string name);
}

/// <summary>
/// 表示权限组定义。
/// </summary>
public interface IPermissionGroupDefinition
{
    /// <summary>
    /// 获取组名称。
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 获取或设置默认显示名，未启用本地化或缺词条时直接展示。
    /// </summary>
    /// <remarks>本地化词条键按约定为 <c>PermissionGroup:{组名}</c>，不写在这里。</remarks>
    string? DisplayName { get; set; }

    /// <summary>
    /// 组内的顶层权限（各自可再带子权限），按声明顺序排列。
    /// </summary>
    IReadOnlyList<IPermissionDefinition> Permissions { get; }

    /// <summary>
    /// 向组添加权限。
    /// </summary>
    /// <param name="name">权限名称</param>
    /// <param name="side">
    /// 多租户侧别，<b>必填</b>。没有默认值是刻意的：省略时静默落到 <c>Both</c>（"租户管理员也拿得到"），
    /// 而宿主全局资源落成 <c>Both</c> 就是跨租户越权，且只在真的建了租户之后才暴露。
    /// 判据是这条权限背后的数据带不带租户维度。
    /// </param>
    /// <param name="displayName">默认显示名（通常是英文），未启用本地化或缺词条时直接展示；词条键按约定由名称拼出。</param>
    /// <returns>权限定义</returns>
    IPermissionDefinition AddPermission(string name, MultiTenancySides side, string? displayName = null);

    /// <summary>
    /// 获取组内的权限定义。
    /// </summary>
    /// <param name="name">权限名称</param>
    /// <returns>权限定义，如果不存在或不属于本组则返回 null</returns>
    IPermissionDefinition? GetPermissionOrNull(string name);
}

/// <summary>
/// 表示权限定义。
/// </summary>
public interface IPermissionDefinition
{
    /// <summary>
    /// 权限名称（唯一标识）
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 获取或设置默认显示名，未启用本地化或缺词条时直接展示。
    /// </summary>
    /// <remarks>本地化词条键按约定为 <c>Permission:{权限名}</c>，不写在这里。</remarks>
    string? DisplayName { get; set; }

    /// <summary>
    /// 获取父权限。
    /// </summary>
    IPermissionDefinition? Parent { get; }

    /// <summary>
    /// 获取子权限集合。
    /// </summary>
    IReadOnlyList<IPermissionDefinition> Children { get; }

    /// <summary>
    /// 获取或设置权限是否启用。
    /// </summary>
    bool IsEnabled { get; set; }

    /// <summary>
    /// 获取权限适用的多租户侧别。
    /// </summary>
    MultiTenancySides Side { get; }

    /// <summary>
    /// 添加子权限。
    /// </summary>
    /// <param name="name">权限名称</param>
    /// <param name="displayName">默认显示名（通常是英文），未启用本地化或缺词条时直接展示；词条键按约定由名称拼出。</param>
    /// <param name="side">多租户侧别；不指定时继承父权限的侧别</param>
    /// <returns>子权限定义</returns>
    IPermissionDefinition AddChild(string name, string? displayName = null, MultiTenancySides? side = null);
}
