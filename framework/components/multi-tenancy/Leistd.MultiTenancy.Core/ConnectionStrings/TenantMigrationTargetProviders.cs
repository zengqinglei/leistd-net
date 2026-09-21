namespace Leistd.MultiTenancy.ConnectionStrings;

// 迁移目标一律从连接存储按名字读：本地宿主拿到的是 EF 实现（读控制库），
// 资源服务拿到的是宿主的 HTTP 实现（回源控制面）。两种形态的差异由注册决定，不需要两个提供器。
internal sealed class TenantMigrationTargetProvider(ITenantConnectionConfigurationStore connectionStore)
    : ITenantMigrationTargetProvider
{
    public async Task<IReadOnlyList<TenantMigrationTarget>> GetDedicatedTargetsAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        var connections = await connectionStore.GetListAsync(name, cancellationToken);

        // 存储已经按名字解析并排除了"一条连接都没有"的租户；这里按物理库合并。
        // 运行时逐库作业不走这条路——它经 ITenantDatabaseDirectory 取指纹与租户归属，
        // 因为这里的每一条都带明文连接串，常驻服务不该拿到
        return
        [
            .. connections
                .Select(x => new TenantMigrationTarget(x.TenantId, x.ConnectionString))
                .GroupBy(x => x.Fingerprint, StringComparer.Ordinal)
                .Select(group => group.MinBy(x => x.TenantId)!)
        ];
    }
}
