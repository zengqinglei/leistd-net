using CompanyName.ProjectName.Domain.Users.Entities;
using CompanyName.ProjectName.Domain.Users.Errors;
using Leistd.Auditing.Abstractions;
using Leistd.Ddd.Domain.DataFilters;
using Leistd.Ddd.Domain.Repositories;
using Leistd.ExceptionHandling;
using Microsoft.Extensions.Logging;

namespace CompanyName.ProjectName.Domain.Users.DomainServices;

/// <summary>
/// 角色领域服务：角色名在租户内唯一。
/// </summary>
public sealed class RoleDomainService(
    IRepository<Role, Guid> roleRepository,
    IDataFilter dataFilter,
    ILogger<RoleDomainService> logger)
{
    /// <summary>
    /// 创建自定义角色（非内置），角色名已被占用时以 <see cref="RoleErrorCodes.NameAlreadyUsed"/> 拒绝。
    /// </summary>
    /// <remarks>
    /// 查重要看见被软删除的行：角色名的唯一索引没有排除 <c>IsDeleted</c>，删掉的角色仍然占着名字，
    /// 而仓储默认把这些行过滤掉。不关掉过滤，这里会答"可用"，随后落库撞唯一索引——
    /// 用户看到的是 500，而不是"角色名已被占用"。
    /// <para>保留租户过滤：唯一索引按租户分开，跨租户允许同名。</para>
    /// </remarks>
    public async Task<Role> CreateAsync(
        string name,
        string displayName,
        string? description,
        bool isDefault,
        int sort,
        CancellationToken cancellationToken = default)
    {
        bool taken;
        using (dataFilter.Disable<ISoftDelete>())
        {
            taken = await roleRepository.AnyAsync(r => r.Name == name, cancellationToken);
        }

        if (taken)
        {
            throw new BusinessException(RoleErrorCodes.NameAlreadyUsed, $"Role '{name}' already exists.")
                .WithData("Name", name);
        }

        var role = new Role(name, displayName, description, isStatic: false, isDefault: isDefault, sort: sort);
        await roleRepository.InsertAsync(role, cancellationToken);

        logger.LogInformation("Role created: {Name} (ID: {Id})", role.Name, role.Id);
        return role;
    }
}
