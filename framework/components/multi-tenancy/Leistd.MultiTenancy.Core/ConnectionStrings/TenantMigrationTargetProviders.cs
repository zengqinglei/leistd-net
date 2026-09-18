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

        // 存储已经按名字解析并排除了"一条连接都没有"的租户；这里只做形态转换，
        // 指纹在 TenantMigrationTarget 上生成，用于日志与去重
        return [.. connections.Select(x => new TenantMigrationTarget(x.TenantId, x.ConnectionString))];
    }
}
