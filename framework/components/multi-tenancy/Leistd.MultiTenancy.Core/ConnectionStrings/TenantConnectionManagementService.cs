using Leistd.EventBus.Abstractions;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.MultiTenancy.Context;
using Leistd.MultiTenancy.Errors;
using Leistd.MultiTenancy.Management;
using Leistd.MultiTenancy.Tenancy;
using Leistd.MultiTenancy.Dtos;
using Leistd.MultiTenancy.Events;
using Leistd.MultiTenancy.Exceptions;
using Leistd.MultiTenancy.Stores;

namespace Leistd.MultiTenancy.ConnectionStrings;

internal sealed class TenantConnectionManagementService(
    ITenantConnectionDirectory directory,
    ITenantConnectionConfigurationStore store,
    ITenantConnectionConfigurationManager manager,
    ITenantStore tenants,
    ITenantDatabaseDirectory databaseDirectory,
    ILocalEventBus? eventBus = null) : ITenantConnectionManagementService
{
    public async Task<TenantDatabaseListOutputDto> GetDatabaseListAsync(
        string name,
        bool activeOnly,
        CancellationToken cancellationToken = default)
    {
        // 不能把原始入参直接交给目录：目录用的是代码级归一化（非法即 ArgumentException → 500），
        // 而这个名字来自 URL，非法是调用方能改对的输入错误
        var normalized = TenantConnectionNames.NormalizeInput(name);
        var listed = await databaseDirectory.GetDatabasesAsync(normalized, activeOnly, cancellationToken);

        return new TenantDatabaseListOutputDto
        {
            Databases = [.. listed.Databases.Select(entry => new TenantDatabaseOutputDto
            {
                Fingerprint = entry.Fingerprint,
                TenantIds = entry.TenantIds
            })],
            FailedTenants = [.. listed.FailedTenants.Select(failure => new TenantDatabaseFailureOutputDto
            {
                TenantId = failure.TenantId,
                Reason = failure.Reason
            })]
        };
    }

    public async Task<IReadOnlyList<TenantConnectionOutputDto>> GetListAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var entries = await directory.ListAsync(tenantId, cancellationToken)
            ?? throw new TenantNotFoundException(tenantId.ToString());

        return [.. entries.Select(entry => new TenantConnectionOutputDto { TenantId = tenantId, Name = entry.Name, Version = entry.Version })];
    }

    public async Task<TenantRuntimeConnectionOutputDto> GetRuntimeAsync(
        Guid tenantId,
        string name,
        CancellationToken cancellationToken = default)
    {
        var lookup = await store.FindAsync(tenantId, TenantConnectionNames.NormalizeInput(name), cancellationToken)
            ?? throw new TenantNotFoundException(tenantId.ToString());

        return new TenantRuntimeConnectionOutputDto
        {
            TenantId = lookup.TenantId,
            HasAnyConnection = lookup.HasAnyConnection,
            Connection = lookup.Connection is { } connection
                ? new TenantConnectionDetailOutputDto
                {
                    Name = connection.Name,
                    ConnectionString = connection.ConnectionString,
                    Version = connection.Version
                }
                : null
        };
    }

    public async Task<IReadOnlyList<TenantMigrationConnectionOutputDto>> GetMigrationListAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        var connections = await store.GetListAsync(TenantConnectionNames.NormalizeInput(name), cancellationToken);
        return [.. connections.Select(connection => new TenantMigrationConnectionOutputDto
        {
            TenantId = connection.TenantId,
            Name = connection.Name,
            ConnectionString = connection.ConnectionString
        })];
    }

    public async Task<TenantConnectionOutputDto> SetAsync(
        Guid tenantId,
        string name,
        UpsertTenantConnectionInputDto input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var configuration = await manager.SetAsync(tenantId, name, input.ConnectionString, input.ExpectedVersion, cancellationToken);

        // 连接决定租户数据落在哪个库，改动必须留痕；记成什么由宿主定，组件只发事件（不含连接串）
        await PublishAsync(
            configuration.TenantId,
            configuration.Name,
            input.ExpectedVersion is null ? TenantConnectionChangeKind.Registered : TenantConnectionChangeKind.Changed,
            configuration.Version,
            cancellationToken);

        return new TenantConnectionOutputDto
        {
            TenantId = configuration.TenantId,
            Name = configuration.Name,
            Version = configuration.Version
        };
    }

    public async Task RemoveAsync(Guid tenantId, string name, long expectedVersion, CancellationToken cancellationToken = default)
    {
        // 先归一化再往下传：删除拿不到写入路径那样的权威返回值（SetAsync 发的是 manager 回的
        // configuration.Name），照原样发事件会让 "CRM" 与 "crm" 在审计里变成两个目标标识
        var normalized = TenantConnectionNames.NormalizeInput(name);
        await manager.RemoveAsync(tenantId, normalized, expectedVersion, cancellationToken);
        await PublishAsync(tenantId, normalized, TenantConnectionChangeKind.Removed, expectedVersion, cancellationToken);
    }

    private async Task PublishAsync(
        Guid tenantId,
        string name,
        TenantConnectionChangeKind change,
        long version,
        CancellationToken cancellationToken)
    {
        if (eventBus is null)
        {
            return;
        }

        // 显示名要在发事件时取快照：订阅者事后按标识反查，拿到的是改名后的名字，或者什么都没有。
        // 写入已经落定，所以查不到只能是并发删除——那种情况留 null，不让留痕本身失败
        var tenant = await tenants.FindAsync(tenantId, cancellationToken);

        await eventBus.PublishAsync(
            new TenantConnectionChangedEvent(tenantId, tenant?.DisplayName ?? tenant?.Name, name, change, version),
            cancellationToken);
    }
}
