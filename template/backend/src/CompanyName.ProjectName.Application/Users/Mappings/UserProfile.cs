#if (LocalIdentity)
using CompanyName.ProjectName.Application.Auth.Dtos;
#endif
using CompanyName.ProjectName.Application.Roles.Dtos;
using CompanyName.ProjectName.Application.Users.Avatars;
using CompanyName.ProjectName.Application.Users.Dtos;
using CompanyName.ProjectName.Domain.Users.Entities;
#if (LocalIdentity)
using CompanyName.ProjectName.Domain.Users.ValueObjects;
#endif
using Leistd.ObjectMapping.Mapster;
using Mapster;
using Leistd.ObjectMapping.Mapster.Mapping;

namespace CompanyName.ProjectName.Application.Users.Mappings;

/// <summary>
/// 用户映射配置
/// </summary>
public class UserProfile : MapsterProfile
{
#if (LocalIdentity)
    /// <summary>MapContext 参数名：调用方已解析好的角色名称。</summary>
    /// <remarks>
    /// 写路径（注册、首次外部登录）在同一事务边界内刚分配完角色，关联行尚未落库，
    /// 靠 <see cref="UserRolesKey"/> 与 <see cref="RolesKey"/> 的实体连接查不到。
    /// 传本键即可绕开回查——调用方本来就知道它刚分配了哪些角色。
    /// </remarks>
    public const string RoleNamesKey = "RoleNames";

    /// <summary>MapContext 参数名：当前时刻，用来判定锁定是否仍在生效。</summary>
    /// <remarks>过期的临时锁定在库里仍是 <c>IsLocked</c>（下次登录失败或成功时才清），只看字段会误报"已锁定"。</remarks>
    public const string NowKey = "Now";
#endif

    /// <summary>MapContext 参数名：用户角色关联行。</summary>
    public const string UserRolesKey = "UserRoles";

    /// <summary>MapContext 参数名：角色实体。</summary>
    public const string RolesKey = "Roles";

    protected override void ConfigureMappings()
    {
#if (LocalIdentity)
        CreateMap<User, UserOutputDto>()
            .Map(dest => dest.Roles, src => ResolveRoles(src))
            .Map(dest => dest.Avatar, src => AvatarUrls.For(src.Id, src.Avatar))
            .Map(dest => dest.IsEmailVerified, src => src.EmailConfirmed)
            .Map(dest => dest.IsTwoFactorEnabled, src => src.TwoFactorEnabled)
            .Ignore(dest => dest.TwoFactorSetupRequired)
            ;
#endif

        CreateMap<User, UserManagementOutputDto>()
            .Map(dest => dest.Avatar, src => AvatarUrls.For(src.Id, src.Avatar))
#if (LocalIdentity)
            .Map(dest => dest.IsEmailVerified, src => src.EmailConfirmed)
            .Map(dest => dest.IsLockedOut, src => ResolveIsLockedOut(src))
            .Map(dest => dest.LockoutEnd, src => ResolveIsLockedOut(src) ? src.LockoutEnd : null)
            .Map(dest => dest.IsTwoFactorEnabled, src => src.TwoFactorEnabled)
#endif
            .Map(dest => dest.Roles, src => ResolveRoleBriefs(src))
            ;
    }

#if (LocalIdentity)
    // 优先用调用方直接给出的角色名；没有时退回实体连接
    private static string[] ResolveRoles(User source)
    {
        if (MapContext.Current?.Parameters.TryGetValue(RoleNamesKey, out var roleNamesObj) == true &&
            roleNamesObj is IEnumerable<string> roleNames)
        {
            return [.. roleNames];
        }

        return [.. ResolveRoleEntities(source).Select(role => role.Name)];
    }

    // 调用方没给时刻时按库里的字段报：宁可多报一个已过期的锁定，也不要漏报一个生效中的
    private static bool ResolveIsLockedOut(User source)
    {
        if (MapContext.Current?.Parameters.TryGetValue(NowKey, out var nowObj) == true && nowObj is DateTime now)
        {
            return source.GetAccessStatus(now) == UserAccessStatus.LockedOut;
        }

        return source.IsLocked;
    }
#endif

    // 复用 RoleProfile 里已注册的 Role → RoleBriefDto，不在这里重复一份字段映射
    private static IReadOnlyList<RoleBriefDto> ResolveRoleBriefs(User source)
        => [.. ResolveRoleEntities(source).Select(role => role.Adapt<RoleBriefDto>())];

    private static List<Role> ResolveRoleEntities(User source)
    {
        if (MapContext.Current?.Parameters.TryGetValue(UserRolesKey, out var userRolesObj) == true &&
            userRolesObj is List<UserRole> userRoles &&
            MapContext.Current?.Parameters.TryGetValue(RolesKey, out var rolesObj) == true &&
            rolesObj is List<Role> roles)
        {
            return [.. userRoles
                .Where(ur => ur.UserId == source.Id)
                .Join(roles, ur => ur.RoleId, r => r.Id, (ur, r) => r)];
        }

        return [];
    }
}
