namespace Leistd.MultiTenancy.Resolution;

/// <summary>
/// 在租户解析贡献者之间传递解析状态。
/// </summary>
/// <param name="serviceProvider">当前（请求）作用域的服务提供器，贡献者从中解析所需服务</param>
public class TenantResolveContext(IServiceProvider serviceProvider)
{
    /// <summary>
    /// 获取当前作用域的服务提供器。
    /// </summary>
    public IServiceProvider ServiceProvider { get; } = serviceProvider;

    /// <summary>
    /// 获取或设置解析出的租户标识或名称。
    /// </summary>
    public string? TenantIdOrName { get; set; }

    /// <summary>
    /// 获取或设置是否已确定解析结果。
    /// </summary>
    public bool Handled { get; set; }

    /// <summary>
    /// 判断是否已解析出租户或确定为宿主。
    /// </summary>
    public bool HasResolvedTenantOrHost() => Handled || TenantIdOrName is not null;
}
