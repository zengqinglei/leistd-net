namespace Leistd.Notifications;

/// <summary>
/// 通知类型约定（字符串）。框架仅提供通用类型常量；业务类型可自定义任意字符串。
/// </summary>
/// <remarks>
/// 通知类型采用 string 而非枚举，避免框架内置业务语义、便于各项目自由扩展。
/// </remarks>
public static class NotificationTypes
{
    /// <summary>系统通知。</summary>
    public const string System = "System";

    /// <summary>业务数据变更。</summary>
    public const string DataChange = "DataChange";

    /// <summary>工作流状态变更。</summary>
    public const string Workflow = "Workflow";
}
