#if (!LocalIdentity)
using Refit;

namespace CompanyName.ProjectName.Infrastructure.TenantConnections;

internal enum RemoteTenantDatabaseMode
{
    SharedDatabase,
    DedicatedDatabase
}

internal sealed record RemoteTenantRuntimeConnectionConfiguration
{
    public required Guid TenantId { get; init; }
    public required RemoteTenantDatabaseMode DatabaseMode { get; init; }
    public string? RuntimeSecretReference { get; init; }
    public required long Version { get; init; }
}

internal sealed record RemoteTenantMigrationConnectionConfiguration
{
    public required Guid TenantId { get; init; }
    public required RemoteTenantDatabaseMode DatabaseMode { get; init; }
    public string? MigrationSecretReference { get; init; }
    public required long Version { get; init; }
}

/// <summary>
/// 本服务向 Identity 回源租户连接配置的内部 HTTP 适配器
/// </summary>
/// <remarks>
/// <para>这是本服务 Infrastructure 自己的窄接口，<b>不是框架抽象</b>——Framework 不认识
/// Identity API，连接解析契约在中立的 <c>Leistd.Data.IConnectionStringResolver</c>。</para>
/// <para>引入 Identity 团队发布的正式 Client 包之后，不必改
/// <see cref="IdentityTenantConnectionStringResolver"/>：在 Infrastructure 里用一个适配器
/// 把正式 Client 桥接到本接口即可，解析、缓存与单飞逻辑都不受影响。</para>
/// <para>下面的路由必须与部署中 Identity 服务实际暴露的租户连接端点一致；不一致时
/// 首次解析租户连接即返回 404 并失败关闭，不会静默连到别的库。</para>
/// </remarks>
internal interface IIdentityTenantConnectionClient
{
    [Get("/api/v1/tenant-connections/runtime/{tenantId}")]
    Task<RemoteTenantRuntimeConnectionConfiguration> GetRuntimeAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    [Get("/api/v1/tenant-connections/migration")]
    Task<IReadOnlyList<RemoteTenantMigrationConnectionConfiguration>> GetMigrationListAsync(
        CancellationToken cancellationToken = default);
}
#endif
