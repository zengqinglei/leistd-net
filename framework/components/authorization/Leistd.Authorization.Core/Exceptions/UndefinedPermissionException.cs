using Leistd.Authorization.Checking;
using Leistd.Authorization.Definitions;
using Leistd.Authorization.Errors;
using Leistd.Authorization.Grants;
using Leistd.Authorization.Management;
using Leistd.Authorization.Subjects;
using Leistd.ExceptionHandling;

namespace Leistd.Authorization.Exceptions;

/// <summary>
/// 表示尝试授予未定义或已禁用的权限。
/// </summary>
/// <remarks>
/// 由写入方抛出，是"未定义权限不落库"的唯一执行点——
/// 应用服务、种子数据与后台任务都不必重复这条判断。错误码为 <see cref="PermissionErrorCodes.UndefinedPermission"/>。
/// </remarks>
public class UndefinedPermissionException : BadRequestException
{
    /// <summary>以未定义或已禁用的权限名构造。</summary>
    /// <param name="permissionNames">未定义或已禁用的权限名。</param>
    public UndefinedPermissionException(IReadOnlyList<string> permissionNames)
        : base(
            $"The following permissions are undefined or disabled and cannot be granted: " +
            $"{string.Join(", ", permissionNames)}.")
    {
        PermissionNames = permissionNames;
        WithCode(PermissionErrorCodes.UndefinedPermission).WithData("Names", string.Join(", ", permissionNames));
    }

    /// <summary>未定义或已禁用的权限名。</summary>
    public IReadOnlyList<string> PermissionNames { get; }
}
