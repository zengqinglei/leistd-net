namespace Leistd.MultiTenancy;

/// <summary>
/// 租户解析上下文，在解析链的贡献者之间传递
/// </summary>
/// <param name="serviceProvider">当前（请求）作用域的服务提供器，贡献者从中解析所需服务</param>
public class TenantResolveContext(IServiceProvider serviceProvider)
{
    /// <summary>
    /// 当前作用域服务提供器
    /// </summary>
    public IServiceProvider ServiceProvider { get; } = serviceProvider;

    /// <summary>
    /// 解析出的租户 Id 或名称（两者皆可，由消费方判定形态）
    /// </summary>
    public string? TenantIdOrName { get; set; }

    /// <summary>
    /// 是否已有定论。<c>true</c> 且 <see cref="TenantIdOrName"/> 为 null 表示"确定是宿主"，同样终止链
    /// </summary>
    public bool Handled { get; set; }

    /// <summary>
    /// 是否已解析出租户或已确定为宿主
    /// </summary>
    public bool HasResolvedTenantOrHost() => Handled || TenantIdOrName is not null;
}
