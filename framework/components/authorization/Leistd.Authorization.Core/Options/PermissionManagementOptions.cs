namespace Leistd.Authorization.Options;

/// <summary>
/// 权限管理用例的展示约定。
/// </summary>
public sealed class PermissionManagementOptions
{
    /// <summary>
    /// 翻译权限与分组显示名的资源类型；<see langword="null"/> 时不翻译。
    /// </summary>
    /// <remarks>
    /// 定义里的 <c>DisplayName</c> 作为词条键查找；查不到或没写时回落到权限名（分组名），
    /// 而不是把带内部前缀的键摆到界面上。
    /// </remarks>
    public Type? LocalizationResource { get; set; }
}
