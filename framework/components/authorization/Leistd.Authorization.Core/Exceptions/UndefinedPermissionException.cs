using Leistd.ExceptionHandling;

namespace Leistd.Authorization.Exceptions;

/// <summary>
/// 表示尝试授予未定义或已禁用的权限。
/// </summary>
/// <remarks>
/// 由写入方抛出，是"未定义权限不落库"的唯一执行点——
/// 应用服务、种子数据与后台任务都不必重复这条判断。
/// </remarks>
/// <param name="permissionNames">未定义或已禁用的权限名。</param>
public class UndefinedPermissionException(IReadOnlyList<string> permissionNames)
    : BadRequestException(
        $"The following permissions are undefined or disabled and cannot be granted: " +
        $"{string.Join(", ", permissionNames)}.")
{
    /// <summary>未定义或已禁用的权限名。</summary>
    public IReadOnlyList<string> PermissionNames { get; } = permissionNames;
}
