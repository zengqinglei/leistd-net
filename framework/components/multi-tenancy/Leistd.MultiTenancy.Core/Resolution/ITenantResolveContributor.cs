namespace Leistd.MultiTenancy.Resolution;

/// <summary>
/// 从单一来源尝试确定当前租户。
/// </summary>
public interface ITenantResolveContributor
{
    /// <summary>
    /// 获取用于诊断的贡献者名称。
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 尝试解析租户并更新解析上下文。
    /// </summary>
    Task ResolveAsync(TenantResolveContext context);
}
