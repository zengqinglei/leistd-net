namespace Leistd.MultiTenancy.Resolution;

/// <summary>
/// 按注册顺序解析当前请求的租户。
/// </summary>
public interface ITenantResolver
{
    /// <summary>
    /// 执行租户解析链。
    /// </summary>
    Task<TenantResolveResult> ResolveAsync();
}

/// <summary>
/// 表示租户解析结果。
/// </summary>
public class TenantResolveResult
{
    /// <summary>
    /// 获取或设置解析出的租户标识或名称；<see langword="null"/> 表示宿主。
    /// </summary>
    public string? TenantIdOrName { get; set; }

    /// <summary>
    /// 获取已执行的贡献者名称。
    /// </summary>
    public List<string> AppliedResolvers { get; } = [];
}
