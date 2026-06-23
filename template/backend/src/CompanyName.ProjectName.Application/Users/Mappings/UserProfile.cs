#if (IncludeIdentity)
using CompanyName.ProjectName.Application.Auth.Dtos;
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
#if (IncludeIdentity)
        CreateMap<User, UserOutputDto>()
            .Map(dest => dest.Roles, src => ResolveRoles(src));
#endif

        CreateMap<User, UserManagementOutputDto>()
            .Map(dest => dest.DisplayName, src => src.Nickname)
#if (IncludeIdentity)
            .Map(dest => dest.IsEmailVerified, src => src.EmailConfirmed)
            .Map(dest => dest.Roles, src => ResolveRoles(src))
#endif
            ;
    }

#if (IncludeIdentity)
    private static string[] ResolveRoles(User source)
    {
        if (MapContext.Current?.Parameters.TryGetValue("UserRoles", out var userRolesObj) == true &&
            userRolesObj is List<UserRole> userRoles &&
            MapContext.Current?.Parameters.TryGetValue("Roles", out var rolesObj) == true &&
            rolesObj is List<Role> roles)
        {
            return userRoles
                .Where(ur => ur.UserId == source.Id)
                .Join(roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name)
                .ToArray();
        }

        return [];
    }
#endif
}
