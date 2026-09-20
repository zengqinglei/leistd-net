using Leistd.MultiTenancy.Abstractions;

namespace Leistd.Authorization.Abstractions;

/// <summary>
/// 提供权限定义树的只读索引。
/// </summary>
public interface IPermissionDefinitionManager
{
    /// <summary>
    /// 按名称查找权限定义；不存在时返回 <see langword="null"/>。
    /// </summary>
    IPermissionDefinition? GetOrNull(string name);

    /// <summary>
    /// 获取所有权限定义。
    /// </summary>
    IEnumerable<IPermissionDefinition> GetAll();

    /// <summary>
    /// 获取全部权限组，用于驱动权限管理界面的分组与树形展示。
    /// </summary>
    IReadOnlyList<IPermissionGroupDefinition> GetGroups();

    /// <summary>
    /// 判断权限是否已定义且实际可用。
    /// </summary>
    /// <remarks>
    /// 权限自身与其全部祖先都必须处于启用状态；定义不存在时返回 <c>false</c>，
    /// 使拼错或数据库残留的权限名默认被拒绝。
    /// </remarks>
    bool IsEffectivelyEnabled(string name);

    /// <summary>
    /// 判断权限在给定侧别上是否可用：已定义、实际启用，且定义的侧别包含 <paramref name="side"/>。
    /// </summary>
    /// <remarks>
    /// 权限检查器与管理界面用同一个判据：租户上下文里宿主侧权限对任何主体都不可用，
    /// 界面上也就不该出现、不该可勾选，否则租户管理员会看到点进去必然 403 的功能。
    /// </remarks>
    /// <param name="name">权限名。</param>
    /// <param name="side">当前侧别。</param>
    bool IsAvailableOn(string name, MultiTenancySides side)
        => IsEffectivelyEnabled(name) && GetOrNull(name)?.Side.HasFlag(side) == true;

    /// <summary>
    /// 获取权限的全部祖先名称，由近到远。定义不存在时返回空集合。
    /// </summary>
    IReadOnlyList<string> GetAncestorNames(string name);

    /// <summary>
    /// 获取权限的全部子孙名称。定义不存在时返回空集合。
    /// </summary>
    IReadOnlyList<string> GetDescendantNames(string name);
}
