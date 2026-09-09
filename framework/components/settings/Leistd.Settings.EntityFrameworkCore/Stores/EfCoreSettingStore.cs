using Microsoft.EntityFrameworkCore;
using Leistd.MultiTenancy.Abstractions;
using Leistd.Settings.Abstractions;
using Leistd.Settings.Definitions;
using Leistd.Settings.EntityFrameworkCore.Entities;
using Leistd.Settings.Exceptions;
using Leistd.UnitOfWork.EntityFrameworkCore.Database;

namespace Leistd.Settings.EntityFrameworkCore.Stores;

/// <summary>
/// 使用 EF Core 持久化设置值。
/// </summary>
/// <remarks>
/// 通过 <see cref="IDbContextProvider{TDbContext}"/> 获取当前边界的上下文与连接。
/// <see cref="SettingRecord.ScopeKey"/> 保证各层级唯一性；租户隔离仍由查询过滤器承担。
/// <para>
/// <see cref="SettingScopes.Host"/> 与宿主的租户级共用同一行（<c>h:t</c>）：宿主视角本就走租户层，
/// 而设置名全局唯一，两者不会撞在一起。它的意义在于<b>禁止</b>租户各存一份。
/// </para>
/// </remarks>
/// <typeparam name="TDbContext">宿主 DbContext 类型（需包含 SettingRecord 配置）。</typeparam>
/// <param name="dbContextProvider">工作单元内的 DbContext 提供器。</param>
/// <param name="currentTenant">当前租户上下文，用于派生 <c>ScopeKey</c>。</param>
public class EfCoreSettingStore<TDbContext>(
    IDbContextProvider<TDbContext> dbContextProvider,
    ICurrentTenant currentTenant) : ISettingStore
    where TDbContext : DbContext
{
    /// <inheritdoc />
    /// <remarks>
    /// 宿主上下文（<c>TenantId</c> 为 <see langword="null"/>）才读得到宿主那一行：租户上下文下
    /// 查询过滤器会滤掉它，专属库形态下连的还是租户自己的库。
    /// </remarks>
    public bool CanAccessHostScope => currentTenant.Id is null;

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string>> GetAllAsync(
        SettingScopes scope,
        string? userId,
        CancellationToken cancellationToken = default)
    {
        var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);
        var scopeKey = BuildScopeKey(scope, userId);

        return await dbContext.Set<SettingRecord>()
            .Where(x => x.ScopeKey == scopeKey)
            .ToDictionaryAsync(x => x.Name, x => x.Value, StringComparer.Ordinal, cancellationToken);
    }

    /// <inheritdoc />
    public async Task SetAsync(
        string name,
        string? value,
        SettingScopes scope,
        string? userId,
        CancellationToken cancellationToken = default)
    {
        var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);
        var scopeKey = BuildScopeKey(scope, userId);

        var record = await dbContext.Set<SettingRecord>()
            .FirstOrDefaultAsync(x => x.ScopeKey == scopeKey && x.Name == name, cancellationToken);

        if (value is null)
        {
            // 清除该层级的值，使读取回落到下一层；本就没有值时不做任何事。
            if (record is not null)
            {
                dbContext.Set<SettingRecord>().Remove(record);
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            return;
        }

        if (record is null)
        {
            // TenantId 由多租户组件在实体进入跟踪时落值，这里不手工赋值；ScopeKey 与它
            // 同刻取自同一个 ICurrentTenant，两者不会指向不同租户。
            dbContext.Set<SettingRecord>().Add(new SettingRecord
            {
                UserId = scope == SettingScopes.User ? userId : null,
                ScopeKey = scopeKey,
                Name = name,
                Value = value
            });
        }
        else
        {
            record.Value = value;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task RemoveAllAsync(CancellationToken cancellationToken = default)
    {
        var dbContext = await dbContextProvider.GetDbContextAsync(cancellationToken);
        // 按当前租户删：查询过滤器已把范围限定在当前租户，不需要也不应该带 ScopeKey 条件——
        // 要清的是该租户下所有层级，含各用户在该租户内的偏好。
        await dbContext.Set<SettingRecord>().ExecuteDeleteAsync(cancellationToken);
    }

    // 用独立层级段区分租户与用户设置，避免用户标识与租户级保留值冲突。
    //
    //   {tenant}:t            租户级（宿主为 h:t）
    //   {tenant}:u:{userId}   用户级
    // userId 跟随 ISettingStore 的签名保持可空——租户级本就传 null。它只在 scope 为 User
    // 时必需，这种「取值取决于另一个参数」的约束类型系统表达不了，只能在下面就地校验。
    private string BuildScopeKey(SettingScopes scope, string? userId)
    {
        var tenantSegment = currentTenant.Id?.ToString("N") ?? "h";

        switch (scope)
        {
            case SettingScopes.Tenant:
                return $"{tenantSegment}:t";

            case SettingScopes.User:
                // 直接消费 Store 时也须校验用户标识，保持 UserId 与 ScopeKey 层级一致。
                ArgumentException.ThrowIfNullOrWhiteSpace(userId);
                return $"{tenantSegment}:u:{userId}";

            case SettingScopes.Host:
                // 进程级设置只有宿主那一行，因此必须在宿主上下文读写：租户上下文下
                // 查询过滤器会把宿主行滤掉（专属库形态下连的还是租户自己的库），
                // 读到的是空、写进去的是租户行——两者都不报错，只是静默不生效。
                // 这条不变式在存储边界上就地拦住，不靠调用方自觉。
                if (currentTenant.Id is not null)
                {
                    throw new HostScopeUnavailableException(tenantId: currentTenant.Id?.ToString());
                }

                return $"{tenantSegment}:t";

            default:
                // None 与 All 不对应任何一行：前者不是层级，后者是「两层都允许」的定义侧标记。
                // 走到这里说明调用方绕过了 ISettingManager 的校验，静默按某一层处理会写错地方。
                throw new ArgumentOutOfRangeException(
                    nameof(scope), scope, "Only Tenant, User and Host scopes address a stored row.");
        }
    }
}
