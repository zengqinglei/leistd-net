#if (TenantWorkspace && LocalIdentity)
using CompanyName.ProjectName.Application.Roles.Dtos;
using CompanyName.ProjectName.Application.Tenants.Dtos;
using CompanyName.ProjectName.Domain.Tenants.Entities;
using CompanyName.ProjectName.Domain.Users.Entities;
using CompanyName.ProjectName.Domain.Users.Errors;
using CompanyName.ProjectName.Domain.Users.Repositories;
using Leistd.Auditing.Abstractions;
using Leistd.Data.Filters;
using Leistd.Data.Querying;
using Leistd.Ddd.Application.AppServices;
using Leistd.Ddd.Domain.Repositories;
using Leistd.ExceptionHandling;
using Leistd.MultiTenancy.Context;
using Leistd.MultiTenancy.Errors;
using Leistd.MultiTenancy.Stores;
using Leistd.Timing;
using Leistd.UnitOfWork;

namespace CompanyName.ProjectName.Application.Tenants.AppServices;

/// <summary>宿主授予与撤销平台人员访问；每个工作单元只访问一个数据库。</summary>
public sealed class TenantStaffAppService(
    IRepository<UserTenantAccess, Guid> accessRepository,
    IUserRepository userRepository,
    IRepository<Role, Guid> roleRepository,
    ITenantStore tenantStore,
    ICurrentTenant currentTenant,
    IUnitOfWorkManager unitOfWorkManager,
    IDataFilter dataFilter,
    IQueryableAsyncExecuter asyncExecuter,
    IClock clock) : BaseAppService, ITenantStaffAppService
{
    /// <summary>候选人单次最多返回的条数；前端 <c>TENANT_STAFF_CANDIDATE_LIMIT</c> 与它一致，满额时提示继续输入。</summary>
    public const int CandidateLimit = 200;

    public async Task<IReadOnlyList<TenantStaffOutputDto>> GetListAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var tenant = await GetTenantAsync(tenantId, cancellationToken);
        var accesses = await accessRepository.GetListAsync(a => a.TenantId == tenantId, cancellationToken);
        var homeIds = accesses.Select(a => a.HomeUserId).ToList();
        List<User> homes;
        using (currentTenant.Change(null))
        using (unitOfWorkManager.Begin(requiresNew: true))
            homes = await asyncExecuter.ToListAsync((await userRepository.GetQueryableAsync(cancellationToken))
                .Where(u => homeIds.Contains(u.Id)), cancellationToken);

        using (currentTenant.Change(tenant.Id, tenant.Name))
        using (unitOfWorkManager.Begin(requiresNew: true))
        {
            var members = await asyncExecuter.ToListAsync((await userRepository.GetQueryableWithRolesAsync(cancellationToken))
                .Where(u => u.HomeUserId != null && homeIds.Contains(u.HomeUserId.Value)), cancellationToken);
            var roles = await roleRepository.GetListAsync(cancellationToken: cancellationToken);
            return members.Join(homes, m => m.HomeUserId, h => h.Id, (member, home) => new TenantStaffOutputDto
            {
                HomeUserId = home.Id, MemberId = member.Id, Username = home.Username, LinkedUsername = member.Username, DisplayName = home.DisplayName,
                IsActive = member.IsActive, CreationTime = accesses.Single(a => a.HomeUserId == home.Id).CreationTime,
                Roles = roles.Where(r => member.IsInRole(r.Id)).Select(RoleOutput).ToList()
            }).OrderBy(s => s.Username).ToList();
        }
    }

    public async Task<IReadOnlyList<RoleBriefOutputDto>> GetRolesAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var tenant = await GetTenantAsync(tenantId, cancellationToken);
        using (currentTenant.Change(tenant.Id, tenant.Name))
        using (unitOfWorkManager.Begin(requiresNew: true))
            return (await roleRepository.GetListAsync(cancellationToken: cancellationToken)).Select(RoleOutput).OrderBy(r => r.Name).ToList();
    }

    public async Task<IReadOnlyList<TenantStaffCandidateOutputDto>> GetCandidatesAsync(Guid tenantId, string? keyword = null,
        CancellationToken cancellationToken = default)
    {
        await GetTenantAsync(tenantId, cancellationToken);
        using (currentTenant.Change(null))
        using (unitOfWorkManager.Begin(requiresNew: true))
        {
            var query = (await userRepository.GetQueryableAsync(cancellationToken)).Where(user => user.HomeUserId == null);
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var term = keyword.Trim().ToLowerInvariant();
                query = query.Where(user => user.Username.ToLower().Contains(term) ||
                    (user.DisplayName != null && user.DisplayName.ToLower().Contains(term)));
            }
            return await asyncExecuter.ToListAsync(query.OrderByDescending(user => user.CreationTime).ThenBy(user => user.Id)
                .Take(CandidateLimit).Select(user => new TenantStaffCandidateOutputDto(user.Id, user.Username, user.DisplayName)), cancellationToken);
        }
    }

    public async Task GrantAsync(Guid tenantId, GrantTenantStaffInputDto input, CancellationToken cancellationToken = default)
    {
        var tenant = await GetTenantAsync(tenantId, cancellationToken);
        User home;
        using (currentTenant.Change(null))
        using (unitOfWorkManager.Begin(requiresNew: true))
            home = await userRepository.GetByIdAsync(input.HomeUserId!.Value, cancellationToken)
                ?? throw new BusinessException(UserErrorCodes.NotFound, "The home account was not found.");

        using (currentTenant.Change(tenant.Id, tenant.Name))
        using (var uow = unitOfWorkManager.Begin(requiresNew: true))
        {
            var roleIds = input.RoleIds.Distinct().ToList();
            var roles = await roleRepository.GetListAsync(r => roleIds.Contains(r.Id), cancellationToken);
            if (roles.Count() != roleIds.Count)
                throw new BusinessException(UserErrorCodes.RolesNotFound, "One or more roles were not found.").WithData("Roles", string.Join(", ", roleIds.Except(roles.Select(role => role.Id))));
            User? member;
            using (dataFilter.Disable<ISoftDelete>())
                member = await asyncExecuter.FirstOrDefaultAsync((await userRepository.GetQueryableWithRolesAsync(cancellationToken))
                    .Where(u => u.HomeUserId == home.Id), cancellationToken);
            if (member is null)
            {
                var username = "platform." + home.Username;
                using (dataFilter.Disable<ISoftDelete>())
                if (await userRepository.AnyAsync(u => u.Username == username, cancellationToken))
                    throw new BusinessException(UserErrorCodes.UsernameTaken, "The linked username is already in use.").WithData("Username", username);
                member = User.CreatePlatformAccount(home.Id, home.Username, home.DisplayName);
                member.ReplaceRoles(roleIds);
                await userRepository.InsertAsync(member, cancellationToken);
            }
            else
            {
                if (member.IsDeleted) member.RestorePlatformAccount();
                member.ReplaceRoles(roleIds);
                await userRepository.UpdateAsync(member, cancellationToken);
            }
            await uow.CompleteAsync(cancellationToken);
        }

        // 先建立成员再写索引；入口同时要求两者存在，未完成的授予不会开放访问。
        if (!await accessRepository.AnyAsync(a => a.HomeUserId == home.Id && a.TenantId == tenantId, cancellationToken))
            await accessRepository.InsertAsync(new UserTenantAccess(home.Id, tenantId, clock.Now), cancellationToken);
    }

    public async Task RemoveAsync(Guid tenantId, Guid homeUserId, CancellationToken cancellationToken = default)
    {
        var tenant = await GetTenantAsync(tenantId, cancellationToken);
        var access = await accessRepository.GetOneAsync(a => a.HomeUserId == homeUserId && a.TenantId == tenantId, cancellationToken);
        if (access is not null) await accessRepository.DeleteAsync(access, cancellationToken);
        using (currentTenant.Change(tenant.Id, tenant.Name))
        using (var uow = unitOfWorkManager.Begin(requiresNew: true))
        {
            var member = await userRepository.GetOneAsync(u => u.HomeUserId == homeUserId, cancellationToken);
            if (member is not null) await userRepository.DeleteAsync(member, cancellationToken);
            await uow.CompleteAsync(cancellationToken);
        }
    }

    private async Task<TenantConfiguration> GetTenantAsync(Guid id, CancellationToken cancellationToken) =>
        await tenantStore.FindAsync(id, cancellationToken)
        ?? throw new BusinessException(MultiTenancyErrorCodes.NotFound, "The tenant was not found.");

    private static RoleBriefOutputDto RoleOutput(Role role) => new() { Id = role.Id, Name = role.Name, DisplayName = role.DisplayName, IsStatic = role.IsStatic };
}
#endif
