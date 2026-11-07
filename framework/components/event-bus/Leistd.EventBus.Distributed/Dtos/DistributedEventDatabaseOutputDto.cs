namespace Leistd.EventBus.Distributed.Dtos;

/// <summary>宿主可选择的物理库；不暴露连接串或指纹。</summary>
public sealed record DistributedEventDatabaseOutputDto
{
    /// <summary>host 或代表租户的 Guid 字符串。</summary>
    public required string Key { get; init; }

    /// <summary>是否宿主库。</summary>
    public required bool IsHost { get; init; }

    /// <summary>用于进入该库的代表租户。</summary>
    public Guid? RepresentativeTenantId { get; init; }

    /// <summary>目录提供的租户清单；宿主项为空，不代表无租户。</summary>
    public required IReadOnlyList<Guid> TenantIds { get; init; }
}
