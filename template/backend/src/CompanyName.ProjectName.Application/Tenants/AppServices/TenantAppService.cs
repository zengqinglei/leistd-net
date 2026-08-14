#if (TenancyEnabled)
using CompanyName.ProjectName.Application.Tenants.Dtos;
using Leistd.Ddd.Application.Contracts.Dtos;
using Leistd.Exception.Core;
using Leistd.MultiTenancy;
using Microsoft.Extensions.Logging;

namespace CompanyName.ProjectName.Application.Tenants.AppServices;

/// <summary>
/// 租户管理应用服务实现
/// </summary>
/// <remarks>
/// 写路径统一走框架 <see cref="ITenantManager"/>（归一化、唯一性校验、存储缓存失效都在那里收口）；
/// 创建后立即经 <see cref="ICurrentTenant.Change"/> 进入新租户上下文执行 <see cref="ITenantSeeder"/>。
/// </remarks>
public class TenantAppService(
    ITenantManager tenantManager,
    ITenantStore tenantStore,
    ITenantNormalizer tenantNormalizer,
    ITenantSeeder tenantSeeder,
    ICurrentTenant currentTenant,
    ILogger<TenantAppService> logger) : ITenantAppService
{
    /// <inheritdoc />
    public async Task<PagedResultDto<TenantOutputDto>> GetPagedAsync(
        GetTenantPagedInputDto input,
        CancellationToken cancellationToken = default)
    {
        var page = await tenantManager.GetPagedAsync(input.Keyword, input.Offset, input.Limit, cancellationToken);
        return new PagedResultDto<TenantOutputDto>(page.TotalCount, page.Items.Select(ToOutputDto).ToList());
    }

    /// <inheritdoc />
    public async Task<TenantOutputDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var record = await tenantManager.FindAsync(id, cancellationToken)
                     ?? throw new NotFoundException($"Tenant '{id}' not found.");
        return ToOutputDto(record);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>创建 = 注册表写入 + 租内种子，两者不在一个数据库事务里：种子内持有分布式锁，
    /// 圈进事务会把锁与事务生命周期绑死；框架的 EF 管理器与仓储也分处不同工作单元作用域，
    /// 一个 <c>[UnitOfWork]</c> 圈不住注册表写入。因此以补偿取代事务。</para>
    /// <para>补偿必须覆盖**已经落库的种子数据**（角色可能已写入而用户尚未），
    /// 而不是只软删注册表——那样旧租户 Id 下会永久残留角色与授权版本，重试也只是换个新 Id。
    /// 先清种子、再删注册表，两步都幂等。</para>
    /// </remarks>
    public async Task<TenantOutputDto> CreateAsync(CreateTenantInputDto input, CancellationToken cancellationToken = default)
    {
        var tenant = await tenantManager.CreateAsync(input.Name, input.DisplayName, cancellationToken);

        try
        {
            // 在新租户上下文内种子：角色、权限授予、租户管理员的所有行由落值拦截器自动归属该租户
            using (currentTenant.Change(tenant.Id, tenant.Name))
            {
                await tenantSeeder.SeedAsync(input.AdminEmail, input.AdminPassword, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "租户 {TenantId} 初始化失败，回滚租户与已写入的种子数据", tenant.Id);

            // 补偿用独立取消令牌：调用方取消（含超时）不应让补偿也被取消，
            // 否则恰恰在最需要清理的路径上留下半成品租户
            using (currentTenant.Change(tenant.Id, tenant.Name))
            {
                await tenantSeeder.PurgeAsync(CancellationToken.None);
            }

            await tenantManager.DeleteAsync(tenant.Id, CancellationToken.None);
            throw;
        }

        logger.LogInformation("已创建租户 {TenantName}（{TenantId}）并完成初始化", tenant.Name, tenant.Id);
        return ToOutputDto(tenant);
    }

    /// <inheritdoc />
    public async Task<TenantOutputDto> UpdateAsync(Guid id, UpdateTenantInputDto input, CancellationToken cancellationToken = default)
    {
        var record = await tenantManager.UpdateAsync(id, input.Name, input.DisplayName, cancellationToken);
        return ToOutputDto(record);
    }

    /// <inheritdoc />
    public async Task<TenantOutputDto> SetActivationAsync(
        Guid id,
        UpdateTenantActivationInputDto input,
        CancellationToken cancellationToken = default)
    {
        var record = await tenantManager.SetActiveAsync(id, input.IsActive, cancellationToken);
        return ToOutputDto(record);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await tenantManager.DeleteAsync(id, cancellationToken);
        logger.LogInformation("已删除租户 {TenantId}（软删除，业务数据保留）", id);
    }

    /// <inheritdoc />
    public async Task<TenantLookupOutputDto?> FindByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var tenant = await tenantStore.FindByNameAsync(tenantNormalizer.NormalizeName(name)!, cancellationToken);
        if (tenant is null)
        {
            return null;
        }

        // 匿名探测只回选择租户所需的最小信息
        return new TenantLookupOutputDto
        {
            Id = tenant.Id,
            Name = tenant.Name,
            DisplayName = tenant.DisplayName,
            IsActive = tenant.IsActive
        };
    }

    private static TenantOutputDto ToOutputDto(TenantConfiguration tenant) => new()
    {
        Id = tenant.Id,
        Name = tenant.Name,
        DisplayName = tenant.DisplayName,
        IsActive = tenant.IsActive,
        CreationTime = tenant.CreationTime
    };
}
#endif
