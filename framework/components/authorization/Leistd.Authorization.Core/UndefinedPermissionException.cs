namespace Leistd.Authorization;

/// <summary>
/// 试图授予未定义或已禁用的权限。
/// </summary>
/// <remarks>
/// 与 <see cref="PermissionGrantConcurrencyException"/> 同样是本组件自有的领域异常：
/// 不依赖 <c>Leistd.Exception.Core</c>，由宿主在应用层映射为 HTTP 语义（400）与展示文案。
///
/// 校验只做在这里——它是写入方，也是唯一能对所有调用路径（应用服务、种子数据、后台任务）
/// 保证"未定义权限不落库"的位置。上层不要重复这条判断，只负责把本异常翻译成对外契约。
/// </remarks>
/// <param name="permissionNames">未定义或已禁用的权限名。</param>
public class UndefinedPermissionException(IReadOnlyList<string> permissionNames)
    : System.Exception($"以下权限未定义或已禁用，无法授予：{string.Join(", ", permissionNames)}。")
{
    /// <summary>未定义或已禁用的权限名。</summary>
    public IReadOnlyList<string> PermissionNames { get; } = permissionNames;
}
