namespace Leistd.MultiTenancy.Resolution;

/// <summary>
/// 配置租户解析链。
/// </summary>
public class TenantResolveOptions
{
    /// <summary>
    /// 获取按顺序执行的租户解析贡献者。
    /// </summary>
    /// <remarks>首个完成解析的贡献者终止解析链。</remarks>
    public IList<ITenantResolveContributor> Contributors { get; } = [];
}
