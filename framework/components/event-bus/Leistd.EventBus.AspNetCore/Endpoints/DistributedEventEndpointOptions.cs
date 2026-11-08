namespace Leistd.EventBus.AspNetCore.Endpoints;

/// <summary>管理端点的授权策略与载荷路由映射选项。</summary>
public sealed class DistributedEventEndpointOptions
{
    /// <summary>读取状态、目录、列表与详情概要的策略。</summary>
    public string ReadPolicy { get; set; } = string.Empty;

    /// <summary>读取载荷、恢复、丢弃与测试发布的策略。</summary>
    public string ManagePolicy { get; set; } = string.Empty;

    /// <summary>是否映射三个载荷读取路由，默认开启；关闭后这些路由不存在。</summary>
    public bool EnablePayloadEndpoints { get; set; } = true;

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ReadPolicy);
        ArgumentException.ThrowIfNullOrWhiteSpace(ManagePolicy);
    }
}
