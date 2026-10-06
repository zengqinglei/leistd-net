using CompanyName.ProjectName.Application.Roles.Dtos;
using CompanyName.ProjectName.Application.Users.Dtos;
using CompanyName.ProjectName.Application.Users.Mappings;
using CompanyName.ProjectName.Domain.Users.Entities;
using Leistd.ObjectMapping.Abstractions;
using Leistd.ObjectMapping.Mapster;
using Mapster;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyName.ProjectName.UnitTests.Application;

/// <summary>
/// 映射配置里的嵌套映射必须沿用本项目登记的那份配置。
/// </summary>
/// <remarks>
/// 在配置里写无参 <c>Adapt&lt;T&gt;()</c> 用的是 Mapster 的全局配置，本项目给 <c>Role → RoleBriefOutputDto</c>
/// 登记的规则在那里不存在，嵌套处静默按约定映射。这里给角色映射加一条非默认规则，看它是否出现在用户的角色列表里。
/// </remarks>
public class UserMappingsTests
{
    private sealed class MarkedRoleBriefs : IRegister
    {
        public void Register(TypeAdapterConfig config) =>
            config.NewConfig<Role, RoleBriefOutputDto>()
                .Map(dest => dest.DisplayName, src => "marked:" + src.DisplayName);
    }

    [Fact]
    public void A_users_roles_are_mapped_with_the_registered_role_rules()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddMapsterObjectMapper(options =>
            {
                options.Configurators.Add(config => config.Scan(typeof(UserMappings).Assembly));
                options.Configurators.Add(config => new MarkedRoleBriefs().Register(config));
            })
            .BuildServiceProvider();
        var role = new Role("member", "Member");
#if (LocalIdentity)
        var user = new User("alice", "alice@example.test");
#else
        var user = new User(Guid.NewGuid(), "alice", "alice@example.test");
#endif

        var dto = provider.GetRequiredService<IObjectMapper>().Map<User, UserManagementOutputDto>(
            user,
            new Dictionary<string, object>
            {
                [UserMappings.UserRolesKey] = new List<UserRole> { new(user.Id, role.Id) },
                [UserMappings.RolesKey] = new List<Role> { role }
            });

        Assert.Equal("marked:Member", Assert.Single(dto.Roles).DisplayName);
    }
}
