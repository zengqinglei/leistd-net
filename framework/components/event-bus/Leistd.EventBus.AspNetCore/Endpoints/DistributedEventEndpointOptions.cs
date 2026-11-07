namespace Leistd.EventBus.AspNetCore.Endpoints;

/// <summary>管理端点的两个具名授权策略，均为必填。</summary>
public sealed class DistributedEventEndpointOptions
{
    /// <summary>读取状态、目录、列表与详情概要的策略。</summary>
    public string ReadPolicy { get; set; } = string.Empty;

    /// <summary>读取载荷、恢复、丢弃与测试发布的策略。</summary>
    public string ManagePolicy { get; set; } = string.Empty;

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ReadPolicy);
        ArgumentException.ThrowIfNullOrWhiteSpace(ManagePolicy);
    }
}
