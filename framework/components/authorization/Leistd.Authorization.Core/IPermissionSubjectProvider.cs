namespace Leistd.Authorization;

/// <summary>
/// 当前权限检查主体提供器。
/// </summary>
public interface IPermissionSubjectProvider
{
    /// <summary>
    /// 获取当前权限检查主体；未登录或无法识别时返回 null。
    /// </summary>
    Task<PermissionSubject?> GetCurrentSubjectAsync(CancellationToken cancellationToken = default);
}
