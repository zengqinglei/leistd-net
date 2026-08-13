namespace Leistd.MultiTenancy;

/// <summary>
/// 租户解析器：按注册顺序执行贡献者链，得出本次请求的租户线索
/// </summary>
public interface ITenantResolver
{
    /// <summary>
    /// 执行解析链
    /// </summary>
    Task<TenantResolveResult> ResolveAsync();
}

/// <summary>
/// 租户解析结果
/// </summary>
public class TenantResolveResult
{
    /// <summary>
    /// 解析出的租户 Id 或名称；<c>null</c> 表示宿主
    /// </summary>
    public string? TenantIdOrName { get; set; }

    /// <summary>
    /// 实际执行过的贡献者名称（诊断用）
    /// </summary>
    public List<string> AppliedResolvers { get; } = [];
}
