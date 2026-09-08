using Leistd.UnitOfWork.EntityFrameworkCore.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Leistd.Timing;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.MultiTenancy.EntityFrameworkCore.Entities;
using Leistd.MultiTenancy.EntityFrameworkCore.Extensions;
using Leistd.MultiTenancy.EntityFrameworkCore.Stores;
using Leistd.MultiTenancy.Exceptions;

namespace Leistd.MultiTenancy.EntityFrameworkCore.Managers;

/// <summary><see cref="ITenantConnectionConfigurationManager"/> 的 EF Core 实现。</summary>
/// <remarks>
/// 与 <see cref="EfCoreTenantManager{TDbContext}"/> 同型：<b>时间由本类填充</b>，
/// 这样即使宿主没有把控制面 DbContext 接入审计层，也仍能得到"配置在何时被改过"。
/// <c>CreatorId</c> / <c>LastModifierId</c> 由宿主审计层补充——控制面 DbContext 需要
/// 同时接上创建审计钩子与 <c>AuditSaveChangesInterceptor</c>，两者缺一就少一半用户字段。
/// </remarks>
public class EfCoreTenantConnectionConfigurationManager<TDbContext>(
    IDbContextProvider<TDbContext> dbContextProvider,
    IClock clock)
    : ITenantConnectionConfigurationManager
    where TDbContext : DbContext
{
    /// <inheritdoc />
    public async Task<TenantConnectionConfiguration> SetAsync(
        Guid tenantId,
        TenantDatabaseMode databaseMode,
        string? runtimeSecretReference,
        string? migrationSecretReference,
        long? expectedVersion,
        CancellationToken cancellationToken = default)
    {
        Validate(databaseMode, runtimeSecretReference, migrationSecretReference);
        var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);

        // 推进租户版本，防止验证后被并发激活。
        var tenant = await dbContext.UndeletedTenants()
            .FirstOrDefaultAsync(x => x.Id == tenantId, cancellationToken);
        if (tenant is null)
        {
            throw new TenantNotFoundException(tenantId.ToString());
        }

        var record = await dbContext.Set<TenantConnectionRecord>()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken);

        if (expectedVersion != record?.Version)
        {
            throw new TenantConnectionVersionConflictException(tenantId, expectedVersion, record?.Version);
        }

        // 改动已有配置要求租户处于停用态。首次创建不受限：配置尚不存在时，
        // 解析器对该租户是直接抛 404、也从不写缓存，因此不存在陈旧路由。
        if (record is not null && tenant.IsActive)
        {
            throw new TenantConnectionChangeRequiresInactiveTenantException(tenantId);
        }

        tenant.Version++;

        var isFirstWrite = record is null;
        var now = clock.Normalize(clock.Now);
        if (record is null)
        {
            record = new TenantConnectionRecord
            {
                TenantId = tenantId,
                Version = 1,
                CreationTime = now
            };
            dbContext.Set<TenantConnectionRecord>().Add(record);
        }
        else
        {
            record.Version++;
            record.LastModificationTime = now;
        }

        record.DatabaseMode = databaseMode;
        record.RuntimeSecretReference = Normalize(runtimeSecretReference);
        record.MigrationSecretReference = Normalize(migrationSecretReference);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception) when (InvolvesThisTenantRow(exception))
        {
            // 租户行冲突表示生命周期已变，不是连接配置版本过期。
            DiscardOwnChanges();
            throw new TenantConcurrencyConflictException(tenantId);
        }
        catch (DbUpdateException exception) when (isFirstWrite && exception is not DbUpdateConcurrencyException)
        {
            // 仅在重读确认并发首创后转换异常，避免误报其他写入错误。
            DiscardOwnChanges();

            var existing = await dbContext.Set<TenantConnectionRecord>()
                .AsNoTracking()
                .AnyAsync(x => x.TenantId == tenantId, cancellationToken);
            if (!existing)
            {
                throw;
            }

            throw new TenantConcurrencyConflictException(tenantId);
        }
        catch (DbUpdateConcurrencyException exception) when (InvolvesThisConnectionRow(exception))
        {
            // 只转换本连接行的并发失败，不掩盖同次保存中其他实体的冲突。
            DiscardOwnChanges();

            var actual = await dbContext.Set<TenantConnectionRecord>()
                .AsNoTracking()
                .Where(x => x.TenantId == tenantId)
                .Select(x => (long?)x.Version)
                .FirstOrDefaultAsync(cancellationToken);

            throw new TenantConnectionVersionConflictException(tenantId, expectedVersion, actual, exception);
        }

        return EfCoreTenantConnectionConfigurationStore<TDbContext>.ToConfiguration(record);

        // 仅恢复本操作的行，保留宿主工作单元中的其他待提交更改。
        void DiscardOwnChanges()
        {
            var connectionEntry = dbContext.Entry(record);
            connectionEntry.State = connectionEntry.State switch
            {
                EntityState.Added => EntityState.Detached,
                EntityState.Modified => Restore(connectionEntry),
                _ => connectionEntry.State
            };

            // 租户行保持跟踪，仅恢复本次推进的版本。
            var tenantEntry = dbContext.Entry(tenant);
            if (tenantEntry.State == EntityState.Modified)
            {
                Restore(tenantEntry);
            }

            static EntityState Restore(EntityEntry entry)
            {
                entry.CurrentValues.SetValues(entry.OriginalValues);
                entry.State = EntityState.Unchanged;
                return EntityState.Unchanged;
            }
        }

        bool InvolvesThisTenantRow(DbUpdateConcurrencyException exception) =>
            exception.Entries.Any(entry =>
                entry.Entity is TenantRecord candidate && candidate.Id == tenantId);

        bool InvolvesThisConnectionRow(DbUpdateConcurrencyException exception) =>
            exception.Entries.Any(entry =>
                entry.Entity is TenantConnectionRecord candidate && candidate.TenantId == tenantId);
    }

    private static void Validate(
        TenantDatabaseMode databaseMode,
        string? runtimeSecretReference,
        string? migrationSecretReference)
    {
        switch (databaseMode)
        {
            case TenantDatabaseMode.SharedDatabase when
                runtimeSecretReference is not null || migrationSecretReference is not null:
                throw new ArgumentException("Shared database configuration cannot contain Secret references.");

            case TenantDatabaseMode.DedicatedDatabase when
                string.IsNullOrWhiteSpace(runtimeSecretReference) ||
                string.IsNullOrWhiteSpace(migrationSecretReference):
                throw new ArgumentException("Dedicated database configuration requires runtime and migration Secret references.");

            case TenantDatabaseMode.SharedDatabase:
            case TenantDatabaseMode.DedicatedDatabase:
                return;

            default:
                throw new ArgumentOutOfRangeException(nameof(databaseMode));
        }
    }

    private static string? Normalize(string? value) => value?.Trim();
}
