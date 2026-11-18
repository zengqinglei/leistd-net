#if (OpenIddictServer)
namespace CompanyName.ProjectName.Application.Users.Dtos;

/// <summary>用户在指定作用域内是否符合内置管理员资格。</summary>
public sealed record AdministratorStatusOutputDto
{
    public required bool IsAdministrator { get; init; }
}
#endif
