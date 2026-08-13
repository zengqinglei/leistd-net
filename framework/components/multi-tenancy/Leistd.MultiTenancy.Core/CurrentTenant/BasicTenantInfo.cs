namespace Leistd.MultiTenancy;

/// <summary>
/// 当前租户环境上下文的不可变快照
/// </summary>
/// <param name="TenantId">租户 Id，<c>null</c> 表示宿主</param>
/// <param name="Name">租户名称（可选，仅用于展示与日志）</param>
public sealed record BasicTenantInfo(Guid? TenantId, string? Name = null);
