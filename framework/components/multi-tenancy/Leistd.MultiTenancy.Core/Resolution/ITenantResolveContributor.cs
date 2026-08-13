namespace Leistd.MultiTenancy;

/// <summary>
/// 租户解析贡献者：解析链中的一环，从某个来源（Claim、请求头、查询串等）尝试确定租户
/// </summary>
public interface ITenantResolveContributor
{
    /// <summary>
    /// 贡献者名称（用于日志与诊断）
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 尝试解析租户。解析到结果时设置 <see cref="TenantResolveContext.TenantIdOrName"/>；
    /// 确定为宿主（无租户）且应终止链时设置 <see cref="TenantResolveContext.Handled"/>
    /// </summary>
    Task ResolveAsync(TenantResolveContext context);
}
