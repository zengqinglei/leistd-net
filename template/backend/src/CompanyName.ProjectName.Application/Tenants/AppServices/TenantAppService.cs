#if (TenancyEnabled)
using CompanyName.ProjectName.Application.Tenants.Dtos;
using Leistd.Ddd.Application.Contracts.Dtos;
using Leistd.Exception.Core;
using Leistd.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;
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
    IServiceScopeFactory serviceScopeFactory,
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
    /// <para><b>初始停用创建，种子完成后才激活。</b>租户一旦启用，多租户中间件就会接受它——
    /// 而此刻它还没有管理员和权限授予，匿名入口（注册端点带 <c>X-Tenant-Id</c>）能进入这个
    /// 半成品租户并在里面留下数据。停用态创建把这个窗口关掉：中间件拒绝停用租户，
    /// 即使补偿删除失败，残留的也是一个不对外服务的租户。</para>
    /// <para>创建 = 注册表写入 + 租内种子，两者不在一个数据库事务里：种子内持有分布式锁，
    /// 圈进事务会把锁与事务生命周期绑死；框架的 EF 管理器与仓储也分处不同工作单元作用域，
    /// 一个 <c>[UnitOfWork]</c> 圈不住注册表写入。因此以补偿取代事务。</para>
    /// <para>补偿必须覆盖**已经落库的种子数据**（角色可能已写入而用户尚未），
    /// 而不是只软删注册表——那样旧租户 Id 下会永久残留角色与授权版本，重试也只是换个新 Id。
    /// 先清种子、再删注册表，两步都幂等。</para>
    /// <para>激活失败不触发补偿：数据已完整，只是没启用——那是安全的失败态，
    /// 管理员在列表里启用即可，删掉一个完整的租户反而是更大的损失。</para>
    /// </remarks>
    public async Task<TenantOutputDto> CreateAsync(CreateTenantInputDto input, CancellationToken cancellationToken = default)
    {
        var tenant = await tenantManager.CreateAsync(
            input.Name, input.DisplayName, isActive: false, cancellationToken);

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
            await CompensateAsync(tenant);
            throw;
        }

        var activated = await tenantManager.SetActiveAsync(tenant.Id, true, cancellationToken);

        logger.LogInformation("已创建租户 {TenantName}（{TenantId}）并完成初始化", tenant.Name, tenant.Id);
        return ToOutputDto(activated);
    }

    /// <summary>
    /// 回滚一次失败的租户创建：先清租内已写入的种子数据，再删除租户注册表。
    /// </summary>
    /// <remarks>
    /// <para><b>必须在独立作用域里执行。</b>失败现场的 DbContext 仍跟踪着写入失败的实体
    /// （EF 在 SaveChanges 失败后不会回滚跟踪状态），用它清理会在下一次保存把那些实体一起
    /// 写进数据库——补偿反而制造残留。新作用域拿到干净的 DbContext，只看已落库的数据。</para>
    /// <para>两步各自兜住异常：补偿失败不能覆盖原始的种子异常（那才是调用方需要看到的原因），
    /// 也不能阻止注册表删除——注册表一删租户即不可达，残留数据虽在但无法被访问。
    /// 补偿自身失败以 Error 日志暴露，交由运维核查；不引入重试队列或对账作业，
    /// 那是分布式事务基础设施，不属于模板范围。</para>
    /// <para>用独立取消令牌：调用方取消（含超时）不应让补偿也被取消，
    /// 否则恰恰在最需要清理的路径上留下半成品租户。</para>
    /// </remarks>
    private async Task CompensateAsync(TenantConfiguration tenant)
    {
        // 清种子与删注册表各用一个新作用域。同一个理由适用两次：一步的 SaveChanges 失败会在
        // 它的跟踪器里留下失败实体，后一步复用这个上下文就会把那些实体带进自己的保存——
        // 删注册表这一步尤其不能被拖累，它是"租户从此不可达"的最后保障。
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
                "清除租户 {TenantId} 的种子数据失败，需人工核查残留数据",
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
                "删除租户 {TenantId} 失败，该名称在人工清理前无法重用",
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
