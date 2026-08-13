using Leistd.MultiTenancy;

namespace Leistd.Authorization;

/// <summary>
/// 权限定义提供者
/// </summary>
/// <remarks>
/// 业务项目可实现此接口来定义权限
/// 示例：AiRelayPermissionDefinitionProvider
/// </remarks>
public interface IPermissionDefinitionProvider
{
    /// <summary>
    /// 定义权限
    /// </summary>
    /// <param name="context">权限定义上下文</param>
    void Define(IPermissionDefinitionContext context);
}

/// <summary>
/// 权限定义上下文
/// </summary>
/// <remarks>
/// 每个权限都必须归属于一个权限组：权限管理界面按组分区渲染，游离权限无法被界面表达。
/// 权限名称在所有组与层级中全局唯一，重复注册会在启动阶段抛出异常。
/// </remarks>
public interface IPermissionDefinitionContext
{
    /// <summary>
    /// 获取或创建权限组
    /// </summary>
    /// <param name="name">组名称</param>
    /// <param name="displayName">显示名称</param>
    /// <param name="side">
    /// 组的多租户侧别（默认两侧通用）。作为组内 <c>AddPermission</c> 未显式指定侧别时的继承默认值
    /// </param>
    /// <returns>权限组</returns>
    IPermissionGroupDefinition GetOrAddGroup(string name, string? displayName = null, MultiTenancySides side = MultiTenancySides.Both);

    /// <summary>
    /// 获取权限定义
    /// </summary>
    /// <param name="name">权限名称</param>
    /// <returns>权限定义，如果不存在则返回 null</returns>
    IPermissionDefinition? GetPermissionOrNull(string name);
}

/// <summary>
/// 权限组定义
/// </summary>
public interface IPermissionGroupDefinition
{
    /// <summary>
    /// 组名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 显示名称
    /// </summary>
    string? DisplayName { get; set; }

    /// <summary>
    /// 组的多租户侧别，作为组内权限未显式指定侧别时的继承默认值
    /// </summary>
    MultiTenancySides Side { get; }

    /// <summary>
    /// 组内的顶层权限（各自可再带子权限），按声明顺序排列。
    /// </summary>
    IReadOnlyList<IPermissionDefinition> Permissions { get; }

    /// <summary>
    /// 添加权限到组
    /// </summary>
    /// <param name="name">权限名称</param>
    /// <param name="displayName">显示名称</param>
    /// <param name="side">多租户侧别；不指定时继承组的侧别</param>
    /// <returns>权限定义</returns>
    IPermissionDefinition AddPermission(string name, string? displayName = null, MultiTenancySides? side = null);

    /// <summary>
    /// 获取组内的权限定义
    /// </summary>
    /// <param name="name">权限名称</param>
    /// <returns>权限定义，如果不存在或不属于本组则返回 null</returns>
    IPermissionDefinition? GetPermissionOrNull(string name);
}

/// <summary>
/// 权限定义
/// </summary>
public interface IPermissionDefinition
{
    /// <summary>
    /// 权限名称（唯一标识）
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 显示名称
    /// </summary>
    string? DisplayName { get; set; }

    /// <summary>
    /// 父权限
    /// </summary>
    IPermissionDefinition? Parent { get; }

    /// <summary>
    /// 子权限集合
    /// </summary>
    IReadOnlyList<IPermissionDefinition> Children { get; }

    /// <summary>
    /// 是否启用
    /// </summary>
    bool IsEnabled { get; set; }

    /// <summary>
    /// 多租户侧别：声明本权限属于宿主侧、租户侧还是两侧通用。
    /// 当前上下文侧别不匹配时，权限检查一律拒绝，定义树与授予界面也应按侧别过滤
    /// </summary>
    MultiTenancySides Side { get; }

    /// <summary>
    /// 添加子权限
    /// </summary>
    /// <param name="name">权限名称</param>
    /// <param name="displayName">显示名称</param>
    /// <param name="side">多租户侧别；不指定时继承父权限的侧别</param>
    /// <returns>子权限定义</returns>
    IPermissionDefinition AddChild(string name, string? displayName = null, MultiTenancySides? side = null);
}

