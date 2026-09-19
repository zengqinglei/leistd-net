#if (!LocalIdentity)
using Refit;

namespace CompanyName.ProjectName.Infrastructure.TenantConnections;

// Identity 经已认证的内部接口下发解密后的连接串；下面的记录都不在默认打印里输出它
internal sealed record RemoteTenantConnectionLookup
{
    public required Guid TenantId { get; init; }
    public required bool HasAnyConnection { get; init; }
    public RemoteTenantConnection? Connection { get; init; }

    public override string ToString() =>
        $"{nameof(RemoteTenantConnectionLookup)} {{ TenantId = {TenantId}, " +
        $"HasAnyConnection = {HasAnyConnection}, Name = {Connection?.Name ?? "<none>"} }}";
}

internal sealed record RemoteTenantConnection
{
    public required string Name { get; init; }
    public required string ConnectionString { get; init; }
    public required long Version { get; init; }

    public override string ToString() =>
        $"{nameof(RemoteTenantConnection)} {{ Name = {Name}, Version = {Version} }}";
}

internal sealed record RemoteTenantMigrationConnection
{
    public required Guid TenantId { get; init; }
    public required string Name { get; init; }
    public required string ConnectionString { get; init; }

    public override string ToString() =>
        $"{nameof(RemoteTenantMigrationConnection)} {{ TenantId = {TenantId}, Name = {Name} }}";
}

/// <summary>
/// 本服务向 Identity 回源租户连接的内部 HTTP 适配器
/// </summary>
/// <remarks>
/// <para>这是本服务 Infrastructure 自己的窄接口，<b>不是框架抽象</b>——Framework 不认识
/// Identity API，连接的契约是 <c>ITenantConnectionConfigurationStore</c>，
/// 由 <see cref="IdentityTenantConnectionStore"/> 适配。</para>
/// <para><b>按名字问、按名字答</b>：连接名就是本服务 DbContext 的 <c>[ConnectionStringName]</c>。
/// 一次只取回被问到的那一条，Identity 不会把该租户在别的服务的连接串也发过来。</para>
/// <para>引入 Identity 团队发布的正式 Client 包之后，只替换 <see cref="IdentityTenantConnectionStore"/>：
/// 解析、缓存与单飞逻辑都在框架里，不受影响。</para>
/// <para>下面的路由必须与部署中 Identity 服务实际暴露的租户连接端点一致；不一致时
/// 首次解析租户连接即返回 404 并失败关闭，不会静默连到别的库。</para>
/// </remarks>
internal interface IIdentityTenantConnectionClient
{
    [Get("/api/v1/tenant-connections/runtime/{tenantId}")]
    Task<RemoteTenantConnectionLookup> GetRuntimeAsync(
        Guid tenantId,
        [Query] string name,
        CancellationToken cancellationToken = default);

    [Get("/api/v1/tenant-connections/migration")]
    Task<IReadOnlyList<RemoteTenantMigrationConnection>> GetMigrationListAsync(
        [Query] string name,
        CancellationToken cancellationToken = default);
}
#endif
