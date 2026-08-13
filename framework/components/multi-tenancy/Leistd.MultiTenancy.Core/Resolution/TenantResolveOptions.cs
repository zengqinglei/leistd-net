namespace Leistd.MultiTenancy;

/// <summary>
/// 租户解析链配置
/// </summary>
public class TenantResolveOptions
{
    /// <summary>
    /// 解析贡献者链，按顺序执行，首个有定论者胜出。
    /// Web 宿主经 <c>AddMultiTenancy()</c> 装配默认链（Claim → Header → QueryString），可增删排序
    /// </summary>
    public IList<ITenantResolveContributor> Contributors { get; } = [];
}
