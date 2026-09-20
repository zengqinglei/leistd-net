using Leistd.Data.Connections;
using Leistd.Data.Paging;
using Leistd.EventBus.Abstractions;
using Leistd.ExceptionHandling;
using Leistd.MultiTenancy.Abstractions;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.MultiTenancy.Dtos;
using Leistd.MultiTenancy.Events;
using Leistd.MultiTenancy.Exceptions;
using Leistd.MultiTenancy.Provisioning;
using Leistd.MultiTenancy.Stores;
using Leistd.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Leistd.MultiTenancy.Services;

internal sealed class TenantManagementService(
    ITenantManager tenantManager,
    ITenantStore tenantStore,
    ITenantNormalizer tenantNormalizer,
    ITenantConnectionConfigurationManager connectionManager,
    ICurrentTenant currentTenant,
    IUnitOfWorkManager unitOfWorkManager,
    IServiceScopeFactory scopeFactory,
    ITenantDatabaseErrorDescriber databaseErrorDescriber,
    IEnumerable<ITenantActivationGuard> activationGuards,
    ILogger<TenantManagementService> logger,
    ITenantProvisioner? provisioner = null,
    ILocalEventBus? eventBus = null) : ITenantManagementService
{
    public async Task<PagedResult<TenantOutputDto>> GetPagedAsync(
        GetTenantPagedInputDto input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        UnprocessableEntityException.ThrowIfInvalid(input);

        var page = await tenantManager.GetPagedAsync(input.Keyword, input, cancellationToken);
        return new PagedResult<TenantOutputDto>(page.TotalCount, page.Items.Select(ToOutput));
    }

    public async Task<TenantOutputDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => ToOutput(await tenantManager.FindAsync(id, cancellationToken) ?? throw new TenantNotFoundException(id.ToString()));

    public async Task<TenantOutputDto> CreateAsync(CreateTenantInputDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        UnprocessableEntityException.ThrowIfInvalid(input);

        // 连接串填错是这条路径上最常见的错误，校验排在任何库操作之前，没道理先建租户再靠补偿擦掉
        var connectionString = string.IsNullOrWhiteSpace(input.ConnectionString)
            ? null
            : TenantConnectionStrings.NormalizeInput(input.ConnectionString);

        // 登记连接的版本仅用于补偿时删除它；返回值必须接住，删除是带版本的乐观并发接口
        long? connectionVersion = null;
        TenantConfiguration tenant;
        using (var controlUnitOfWork = await unitOfWorkManager.BeginAsync(requiresNew: true))
        {
            tenant = await tenantManager.CreateAsync(input.Name, input.DisplayName, isActive: false, input.Description, cancellationToken);

            // 分库在开通之前定案，且与登记租户同一个工作单元：不会留下"有租户没连接"或反过来的半截状态
            if (connectionString is not null)
            {
                var connection = await connectionManager.SetAsync(
                    tenant.Id,
                    ConnectionStringNames.Default,
                    connectionString,
                    expectedVersion: null,
                    cancellationToken);
                connectionVersion = connection.Version;
            }

            await controlUnitOfWork.CompleteAsync(cancellationToken);
        }

        var context = new TenantProvisioningContext(tenant, input);
        TenantConfiguration activated;
        try
        {
            if (provisioner is not null)
            {
                using (currentTenant.Change(tenant.Id, tenant.Name))
                using (var tenantUnitOfWork = await unitOfWorkManager.BeginAsync(requiresNew: true))
                {
                    await provisioner.ProvisionAsync(context, cancellationToken);
                    await tenantUnitOfWork.CompleteAsync(cancellationToken);
                }
            }

            using var activationUnitOfWork = await unitOfWorkManager.BeginAsync(requiresNew: true);
            activated = await tenantManager.SetActiveAsync(tenant.Id, true, cancellationToken);
            await activationUnitOfWork.CompleteAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Tenant {TenantId} initialization failed; rolling back the tenant and its provisioned data", tenant.Id);
            await CompensateAsync(context, connectionVersion);

            if (databaseErrorDescriber.Describe(exception) is { } described)
            {
                throw described;
            }

            throw;
        }

        logger.LogInformation("Tenant {TenantName} ({TenantId}) created and initialized", tenant.Name, tenant.Id);
        await PublishAsync(activated, TenantChangeKind.Created, cancellationToken);
        return ToOutput(activated);
    }

    public async Task<TenantOutputDto> UpdateAsync(Guid id, UpdateTenantInputDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        UnprocessableEntityException.ThrowIfInvalid(input);

        var tenant = await tenantManager.UpdateAsync(id, input.Name, input.DisplayName, input.Description, cancellationToken);
        await PublishAsync(tenant, TenantChangeKind.Updated, cancellationToken);
        return ToOutput(tenant);
    }

    public async Task<TenantOutputDto> SetActivationAsync(
        Guid id,
        UpdateTenantActivationInputDto input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.IsActive)
        {
            // 存在性先判：不存在的租户里"没有用户"同样成立，不先判就会把 404 讲成前置条件不满足
            var existing = await tenantManager.FindAsync(id, cancellationToken) ?? throw new TenantNotFoundException(id.ToString());
            foreach (var guard in activationGuards)
            {
                await guard.EnsureCanActivateAsync(existing, cancellationToken);
            }
        }

        var tenant = await tenantManager.SetActiveAsync(id, input.IsActive, cancellationToken);
        await PublishAsync(tenant, TenantChangeKind.ActivationChanged, cancellationToken);
        return ToOutput(tenant);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // 名字在删除前取：删完什么都查不到，而审计要回答的正是"当时删掉的是哪一个"
        var doomed = await tenantManager.FindAsync(id, cancellationToken);

        await tenantManager.DeleteAsync(id, cancellationToken);
        logger.LogInformation("Tenant {TenantId} deleted (soft delete; business data retained)", id);

        if (eventBus is not null)
        {
            await eventBus.PublishAsync(
                new TenantChangedEvent(id, doomed is null ? null : doomed.DisplayName ?? doomed.Name, TenantChangeKind.Deleted),
                cancellationToken);
        }
    }

    public async Task<TenantLookupOutputDto?> FindByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var tenant = await tenantStore.FindByNameAsync(tenantNormalizer.NormalizeName(name)!, cancellationToken);
        return tenant is null
            ? null
            : new TenantLookupOutputDto { Id = tenant.Id, Name = tenant.Name, DisplayName = tenant.DisplayName, IsActive = tenant.IsActive };
    }

    // 回滚一次失败的创建：清开通数据 → 删连接登记 → 删租户。
    // 每步用干净的作用域（不复用失败 DbContext 的跟踪状态）、独立捕获并记录，前一步失败仍继续后一步，
    // 且用独立令牌，不随调用方取消而中止。删连接必须排在删租户之前：已删租户的连接行再也删不掉。
    private async Task CompensateAsync(TenantProvisioningContext context, long? connectionVersion)
    {
        var tenant = context.Tenant;
        if (provisioner is not null)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var services = scope.ServiceProvider;
                using (services.GetRequiredService<ICurrentTenant>().Change(tenant.Id, tenant.Name))
                using (var unitOfWork = await services.GetRequiredService<IUnitOfWorkManager>().BeginAsync(requiresNew: true))
                {
                    await services.GetRequiredService<ITenantProvisioner>().PurgeAsync(context, CancellationToken.None);
                    await unitOfWork.CompleteAsync(CancellationToken.None);
                }
            }
            catch (Exception purgeError)
            {
                logger.LogError(purgeError, "Failed to purge provisioned data for tenant {TenantId}; residual data requires manual inspection", tenant.Id);
            }
        }

        if (connectionVersion is { } version)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var services = scope.ServiceProvider;
                using var unitOfWork = await services.GetRequiredService<IUnitOfWorkManager>().BeginAsync(requiresNew: true);
                await services.GetRequiredService<ITenantConnectionConfigurationManager>()
                    .RemoveAsync(tenant.Id, ConnectionStringNames.Default, version, CancellationToken.None);
                await unitOfWork.CompleteAsync(CancellationToken.None);
            }
            catch (Exception removeError)
            {
                logger.LogError(
                    removeError,
                    "Failed to remove the connection registration for tenant {TenantId}; an orphan row now points at a deleted tenant",
                    tenant.Id);
            }
        }

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var services = scope.ServiceProvider;
            using var unitOfWork = await services.GetRequiredService<IUnitOfWorkManager>().BeginAsync(requiresNew: true);
            await services.GetRequiredService<ITenantManager>().DeleteAsync(tenant.Id, CancellationToken.None);
            await unitOfWork.CompleteAsync(CancellationToken.None);
        }
        catch (Exception deleteError)
        {
            logger.LogError(deleteError, "Failed to delete tenant {TenantId}; its name cannot be reused until manually cleaned up", tenant.Id);
        }
    }

    private async Task PublishAsync(TenantConfiguration tenant, TenantChangeKind change, CancellationToken cancellationToken)
    {
        if (eventBus is not null)
        {
            await eventBus.PublishAsync(new TenantChangedEvent(tenant.Id, tenant.DisplayName ?? tenant.Name, change), cancellationToken);
        }
    }


    private static TenantOutputDto ToOutput(TenantConfiguration tenant) => new()
    {
        Id = tenant.Id,
        Name = tenant.Name,
        DisplayName = tenant.DisplayName,
        Description = tenant.Description,
        IsActive = tenant.IsActive,
        CreationTime = tenant.CreationTime
    };
}
