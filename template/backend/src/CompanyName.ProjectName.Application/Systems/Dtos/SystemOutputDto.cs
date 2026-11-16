#if (OpenIddictServer || RemoteTokenAuth)
namespace CompanyName.ProjectName.Application.Systems.Dtos;

/// <summary>系统切换入口；进入目标后由目标系统判定权限。</summary>
public sealed record SystemOutputDto
{
    public required string Key { get; init; }
    public required string DisplayName { get; init; }
    public required string HomeUrl { get; init; }
    public required bool IsCurrent { get; init; }
}
#endif
