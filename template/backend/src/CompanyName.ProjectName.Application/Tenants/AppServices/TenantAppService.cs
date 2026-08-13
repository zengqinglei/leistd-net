#if (IncludeTenancy)
using CompanyName.ProjectName.Application.Tenants.Dtos;
using Leistd.Ddd.Application.Contracts.Dtos;
using Leistd.Exception.Core;
using Leistd.MultiTenancy;
using Leistd.MultiTenancy.EntityFrameworkCore;
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
    public async Task<TenantOutputDto> CreateAsync(CreateTenantInputDto input, CancellationToken cancellationToken = default)
    {
        var record = await tenantManager.CreateAsync(input.Name, input.DisplayName, cancellationToken);

        // 在新租户上下文内种子：角色、权限授予、租户管理员的所有行由落值拦截器自动归属该租户
        using (currentTenant.Change(record.Id, record.Name))
        {
            await tenantSeeder.SeedAsync(input.AdminEmail, input.AdminPassword, cancellationToken);
        }

        logger.LogInformation("已创建租户 {TenantName}（{TenantId}）并完成初始化", record.Name, record.Id);
        return ToOutputDto(record);
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

        var configuration = await tenantStore.FindByNameAsync(tenantNormalizer.NormalizeName(name)!, cancellationToken);
        if (configuration is null)
        {
            return null;
        }

        // 匿名探测只回选择租户所需的最小信息；DisplayName 需要读记录，经管理读路径取
        var record = await tenantManager.FindAsync(configuration.Id, cancellationToken);
        return new TenantLookupOutputDto
        {
            Id = configuration.Id,
            Name = configuration.Name,
            DisplayName = record?.DisplayName,
            IsActive = configuration.IsActive
        };
    }

    private static TenantOutputDto ToOutputDto(TenantRecord record) => new()
    {
        Id = record.Id,
        Name = record.Name,
        DisplayName = record.DisplayName,
        IsActive = record.IsActive,
        CreationTime = record.CreationTime
    };
}
#endif
