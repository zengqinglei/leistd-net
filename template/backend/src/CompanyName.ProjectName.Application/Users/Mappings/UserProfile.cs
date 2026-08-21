#if (IdentityService)
using CompanyName.ProjectName.Application.Auth.Dtos;
#endif
#if (LocalAuthorization)
using CompanyName.ProjectName.Application.Roles.Dtos;
#endif
using CompanyName.ProjectName.Application.Users.Dtos;
using CompanyName.ProjectName.Domain.Users.Entities;
using Leistd.ObjectMapping.Mapster;
using Mapster;

namespace CompanyName.ProjectName.Application.Users.Mappings;

/// <summary>
/// 用户映射配置
/// </summary>
public class UserProfile : MapsterProfile
{
    protected override void ConfigureMappings()
    {
#if (IdentityService)
        CreateMap<User, UserOutputDto>()
#if (LocalAuthorization)
            .Map(dest => dest.Roles, src => ResolveRoles(src))
#endif
            ;
#endif

        CreateMap<User, UserManagementOutputDto>()
#if (IdentityService)
            .Map(dest => dest.IsEmailVerified, src => src.EmailConfirmed)
#endif
#if (LocalAuthorization)
            .Map(dest => dest.Roles, src => ResolveRoleBriefs(src))
#endif
            ;
    }

#if (LocalAuthorization)
    private static string[] ResolveRoles(User source)
        => [.. ResolveRoleEntities(source).Select(role => role.Name)];

    private static IReadOnlyList<RoleBriefDto> ResolveRoleBriefs(User source)
        => [.. ResolveRoleEntities(source).Select(role => new RoleBriefDto
        {
            Id = role.Id,
            Name = role.Name,
            DisplayName = role.DisplayName
        })];

    private static List<Role> ResolveRoleEntities(User source)
    {
        if (MapContext.Current?.Parameters.TryGetValue("UserRoles", out var userRolesObj) == true &&
            userRolesObj is List<UserRole> userRoles &&
            MapContext.Current?.Parameters.TryGetValue("Roles", out var rolesObj) == true &&
            rolesObj is List<Role> roles)
        {
            return [.. userRoles
                .Where(ur => ur.UserId == source.Id)
                .Join(roles, ur => ur.RoleId, r => r.Id, (ur, r) => r)];
        }

        return [];
    }
#endif
}
