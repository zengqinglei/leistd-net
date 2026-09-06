using CompanyName.ProjectName.Application.Tenants.Dtos;
using CompanyName.ProjectName.Domain.Users.Entities;
using Leistd.Ddd.Application.Contracts.Dtos;
using Leistd.Ddd.Domain.Repositories;
using Leistd.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Leistd.UnitOfWork;
using Leistd.ExceptionHandling;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.MultiTenancy.Stores;
using Leistd.MultiTenancy.Abstractions;
using Leistd.ObjectMapping.Abstractions;

namespace CompanyName.ProjectName.Application.Tenants.AppServices;

/// <summary>
/// 租户管理应用服务实现
/// </summary>
/// <remarks>
/// 写路径统一走框架 <see cref="ITenantManager"/>（归一化与唯一性校验在那里收口）；
/// 创建后立即经 <see cref="ICurrentTenant.Change"/> 进入新租户上下文执行 <see cref="ITenantSeeder"/>。
/// </remarks>
public class TenantAppService(
    ITenantManager tenantManager,
    ITenantConnectionConfigurationManager connectionConfigurationManager,
    ITenantStore tenantStore,
    ITenantNormalizer tenantNormalizer,
    ITenantSeeder tenantSeeder,
    ICurrentTenant currentTenant,
    IRepository<User, Guid> userRepository,
    IUnitOfWorkManager unitOfWorkManager,
    IServiceScopeFactory serviceScopeFactory,
    IObjectMapper objectMapper,
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
    /// 租户先以停用态写入注册表，连接配置提交后在租户上下文中播种，完成后才激活。
    /// 注册表与租户数据库不共享事务，因此失败时以幂等补偿清除种子数据并删除注册表记录。
    /// 激活也属于补偿边界；存储直接读库，状态在提交后生效，不存在额外缓存失效步骤。
    /// </remarks>
    public async Task<TenantOutputDto> CreateAsync(CreateTenantInputDto input, CancellationToken cancellationToken = default)
    {
        TenantConfiguration tenant;
        using (var controlUnitOfWork = await unitOfWorkManager.BeginAsync(requiresNew: true))
        {
            tenant = await tenantManager.CreateAsync(
                input.Name, input.DisplayName, isActive: false, cancellationToken: cancellationToken);

            await connectionConfigurationManager.SetAsync(
                tenant.Id,
                input.DatabaseMode,
                input.RuntimeSecretReference,
                input.MigrationSecretReference,
                // 租户刚在同一事务里创建，连接配置必然尚不存在
                expectedVersion: null,
                cancellationToken);
            await controlUnitOfWork.CompleteAsync(cancellationToken);
        }

        TenantConfiguration activated;
        try
        {
            // 在新租户上下文内种子：角色、权限授予、租户管理员的所有行由落值拦截器自动归属该租户
            using (currentTenant.Change(tenant.Id, tenant.Name))
            using (var businessUnitOfWork = await unitOfWorkManager.BeginAsync(requiresNew: true))
            {
                await tenantSeeder.SeedAsync(input.AdminEmail, input.AdminPassword, cancellationToken);
                await businessUnitOfWork.CompleteAsync(cancellationToken);
            }

            // 激活失败与播种失败使用同一补偿路径。
            using var activationUnitOfWork = await unitOfWorkManager.BeginAsync(requiresNew: true);
            activated = await tenantManager.SetActiveAsync(tenant.Id, true, cancellationToken);
            await activationUnitOfWork.CompleteAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Tenant {TenantId} initialization failed; rolling back the tenant and seeded data", tenant.Id);
            await CompensateAsync(tenant);
            throw;
        }

        logger.LogInformation("Tenant {TenantName} ({TenantId}) created and initialized", tenant.Name, tenant.Id);
        return ToOutputDto(activated);
    }

    /// <summary>
    /// 回滚一次失败的租户创建：先清租内已写入的种子数据，再删除租户注册表。
    /// </summary>
    /// <remarks>
    /// 清种子与删注册表分别使用干净的依赖注入作用域，避免复用失败 DbContext 的跟踪状态。
    /// 两步独立捕获并记录异常，且不覆盖触发补偿的原始异常；即使清种子失败仍继续删除注册表。
    /// 补偿使用独立取消令牌，不随调用方取消而中止。
    /// </remarks>
    private async Task CompensateAsync(TenantConfiguration tenant)
    {
        // 两个步骤各用干净作用域，避免前一步失败的跟踪状态污染后一步。
        try
        {
            using var purgeScope = serviceScopeFactory.CreateScope();
            var scopedCurrentTenant = purgeScope.ServiceProvider.GetRequiredService<ICurrentTenant>();
            var scopedSeeder = purgeScope.ServiceProvider.GetRequiredService<ITenantSeeder>();

            using (scopedCurrentTenant.Change(tenant.Id, tenant.Name))
            {
                await scopedSeeder.PurgeAsync(CancellationToken.None);
            }
        }
        catch (Exception purgeError)
        {
            logger.LogError(
                purgeError,
                "Failed to purge seed data for tenant {TenantId}; residual data requires manual inspection",
                tenant.Id);
        }

        try
        {
            using var deleteScope = serviceScopeFactory.CreateScope();
            var scopedManager = deleteScope.ServiceProvider.GetRequiredService<ITenantManager>();

            await scopedManager.DeleteAsync(tenant.Id, CancellationToken.None);
        }
        catch (Exception deleteError)
        {
            logger.LogError(
                deleteError,
                "Failed to delete tenant {TenantId}; its name cannot be reused until manually cleaned up",
                tenant.Id);
        }
    }

    /// <inheritdoc />
    public async Task<TenantOutputDto> UpdateAsync(Guid id, UpdateTenantInputDto input, CancellationToken cancellationToken = default)
    {
        var record = await tenantManager.UpdateAsync(id, input.Name, input.DisplayName, cancellationToken);
        return ToOutputDto(record);
    }

    /// <inheritdoc />
    /// <remarks>
    /// 启用前要求租户里至少有一个用户。这条规则本身就站得住——启用一个没有管理员的租户
    /// 毫无用途，只会成为匿名入口（注册、找回密码）的靶子；它同时挡住了控制面竞争：
    /// 另一个宿主管理员在创建流程的播种阶段抢先手动启用，会把一个还没有管理员的
    /// 半成品租户暴露出去。管理员写入之后再抢先启用则无害——租户功能上已经完整。
    /// </remarks>
    public async Task<TenantOutputDto> SetActivationAsync(
        Guid id,
        UpdateTenantActivationInputDto input,
        CancellationToken cancellationToken = default)
    {
        if (input.IsActive)
        {
            // 存在性必须先判：不存在的租户里"用户数为 0"同样成立，
            // 不先判就会把 404 讲成"这个租户还没有用户"（400）
            _ = await tenantManager.FindAsync(id, cancellationToken)
                ?? throw new NotFoundException($"Tenant '{id}' not found.");

            await EnsureTenantHasUsersAsync(id, cancellationToken);
        }

        var record = await tenantManager.SetActiveAsync(id, input.IsActive, cancellationToken);
        return ToOutputDto(record);
    }

    /// <summary>
    /// 校验目标租户内已存在用户；空租户不允许被启用。调用前需已确认租户存在。
    /// </summary>
    private async Task EnsureTenantHasUsersAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        using (currentTenant.Change(tenantId))
        {
            if (await userRepository.CountAsync(cancellationToken: cancellationToken) == 0)
            {
                throw new BadRequestException("This tenant has no users yet; activating it would let nobody in. Finish provisioning first.");
            }
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await tenantManager.DeleteAsync(id, cancellationToken);
        logger.LogInformation("Tenant {TenantId} deleted (soft delete; business data retained)", id);
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

        // 匿名探测只回选择租户所需的最小信息——该投影由 TenantLookupOutputDto 的字段集表达
        return objectMapper.Map<TenantConfiguration, TenantLookupOutputDto>(tenant);
    }

    private TenantOutputDto ToOutputDto(TenantConfiguration tenant)
        => objectMapper.Map<TenantConfiguration, TenantOutputDto>(tenant);
}
