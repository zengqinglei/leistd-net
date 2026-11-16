#if (RemoteTokenAuth)
namespace CompanyName.ProjectName.Domain.Systems.Abstractions;

/// <summary>签发方登记的系统入口。</summary>
public sealed record SystemDirectoryEntry
{
    public required string Key { get; init; }
    public required string DisplayName { get; init; }
    public required string HomeUrl { get; init; }
}
#endif
