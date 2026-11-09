namespace Leistd.Settings.AspNetCore.Endpoints;

/// <summary>共享设置端点的授权口径。</summary>
/// <remarks>策略名必填，组件不内置默认策略；漏配时 <c>MapSharedSettings</c> 映射即抛出。</remarks>
public sealed class SharedSettingEndpointOptions
{
    /// <summary>读取共享设置所需的授权策略名，通常是"持有指定 scope 的机器主体"。</summary>
    public string Policy { get; set; } = string.Empty;

    // 缺必填项抛 ArgumentException，ParamName 即属性名
    internal void Validate() => ArgumentException.ThrowIfNullOrWhiteSpace(Policy);
}
