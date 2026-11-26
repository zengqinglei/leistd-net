#if (OpenIddictServer)
namespace CompanyName.ProjectName.Application.Auth.Dtos;

/// <summary>授权请求失效后可返回的已登记应用。</summary>
public sealed record InteractionApplicationOutputDto(string DisplayName, string HomeUrl);
#endif
