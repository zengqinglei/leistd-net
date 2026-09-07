using Microsoft.EntityFrameworkCore;
using Leistd.MultiTenancy.Abstractions;
using Leistd.Settings.Abstractions;
using Leistd.Settings.Definitions;
using Leistd.Settings.EntityFrameworkCore.Entities;
using Leistd.UnitOfWork.EntityFrameworkCore.Database;

namespace Leistd.Settings.EntityFrameworkCore.Stores;

/// <summary>
/// 使用 EF Core 持久化设置值。
/// </summary>
/// <remarks>
/// DbContext 一律经 <see cref="IDbContextProvider{TDbContext}"/> 获取，不直接注入
/// <typeparamref name="TDbContext"/>：只有它会设置 <c>DbContextCreationContext.Current</c>，
/// 宿主的 <c>AddDbContext</c> 回调据此拿到本工作单元已解析的连接。直接注入会让独立库租户的
/// 设置落到宿主配置的默认连接上，且不进工作单元事务——两者都是静默的。
/// <para>行的定位一律走 <c>ScopeKey</c>：它非空，因此唯一索引对宿主级与租户级也真正生效
/// （见 <see cref="SettingRecord.ScopeKey"/>）。租户隔离仍由多租户查询过滤器承担，
/// <c>ScopeKey</c> 里带租户只是为了让约束覆盖到宿主行。</para>
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

    // 层级标记放在用户标识之前，租户级不带标识、用户级带：这样任意用户标识都不可能
    // 生成租户级的键。用「保留值」区分（比如拿某个字面量当租户级）行不通——契约只要求
    // 用户标识非空白，恰好等于那个保留值的用户就会覆盖掉整个租户的默认值。
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
                // 用户级必须带标识：缺了它会写出 UserId 为 null（按实体契约即租户级）
                // 而 ScopeKey 是用户级的行——两个字段各说各话，正是 ScopeKey 要避免的状态。
                // ISettingManager 已经拦过一道，但本契约是公开的，直接消费它的宿主同样要挡住。
                ArgumentException.ThrowIfNullOrWhiteSpace(userId);
                return $"{tenantSegment}:u:{userId}";

            default:
                // None 与 All 不对应任何一行：前者不是层级，后者是「两层都允许」的定义侧标记。
                // 走到这里说明调用方绕过了 ISettingManager 的校验，静默按某一层处理会写错地方。
                throw new ArgumentOutOfRangeException(
                    nameof(scope), scope, "Only Tenant and User scopes address a stored row.");
        }
    }
}
