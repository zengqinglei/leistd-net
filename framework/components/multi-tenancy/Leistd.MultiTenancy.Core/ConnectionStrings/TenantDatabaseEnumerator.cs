namespace Leistd.MultiTenancy.ConnectionStrings;

// 物理库清单取自迁移目标（去重与代表租户的规则在那里，只此一处）；
// 这里只补上宿主库、去掉连接串——运行时只需要"有哪些库、用哪个租户进去"。
//
// 迁移目标提供器随租户连接解析一起注册（本地或远端）。宿主没有注册任何连接解析时，
// 所有租户都在宿主库里，清单就只有宿主库——注入缺省为 null 正表达这一点，
// 宿主因此不必为"单库模式"另写实现，也不必按模式分支注册。
internal sealed class TenantDatabaseEnumerator(ITenantMigrationTargetProvider? targetProvider = null)
    : ITenantDatabaseEnumerator
{
    public async Task<IReadOnlyList<TenantDatabase>> GetDatabasesAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        if (targetProvider is null)
        {
            return [TenantDatabase.Host];
        }

        var targets = await targetProvider.GetDedicatedTargetsAsync(name, cancellationToken);

        return [TenantDatabase.Host, .. targets.Select(target => new TenantDatabase(target.TenantId, target.Fingerprint))];
    }
}
