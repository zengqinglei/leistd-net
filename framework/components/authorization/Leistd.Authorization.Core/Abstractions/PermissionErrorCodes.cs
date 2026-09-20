namespace Leistd.Authorization.Abstractions;

/// <summary>
/// 权限组件抛出的错误码，默认译文随包分发，宿主资源里的同名词条优先。
/// </summary>
public static class PermissionErrorCodes
{
    /// <summary>当前身份不是可做权限检查的主体（401）。</summary>
    public const string SubjectUnavailable = "Permission:SubjectUnavailable";

    /// <summary>授予主体不存在（404）。占位：<c>Provider</c>、<c>Key</c>。</summary>
    public const string SubjectNotFound = "Permission:SubjectNotFound";

    /// <summary>乐观并发冲突，需重新加载（409）。</summary>
    public const string ConcurrencyConflict = "Permission:ConcurrencyConflict";

    /// <summary>授予未定义或已禁用的权限（400）。占位：<c>Name</c>。</summary>
    public const string UndefinedPermission = "Permission:UndefinedPermission";
}
