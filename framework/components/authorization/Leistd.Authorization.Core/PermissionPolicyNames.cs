namespace Leistd.Authorization;

/// <summary>
/// 授权策略名的书写约定。
/// </summary>
/// <remarks>
/// 定义在 Core 而非 AspNetCore：权限定义注册要用它拒绝含保留字符的权限名，
/// 策略解析要用它拆分策略名，两处必须是同一个值。
/// </remarks>
public static class PermissionPolicyNames
{
    /// <summary>
    /// 「任一满足」策略名的分隔符，如 <c>"App.A|App.B"</c>。权限名自身不得包含该字符。
    /// </summary>
    public const char AnyOfSeparator = '|';
}
