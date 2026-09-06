namespace Leistd.MultiTenancy.ConnectionStrings;

/// <summary>
/// 租户连接配置快照。只包含 Secret 引用，不包含连接字符串明文。
/// </summary>
public sealed class TenantConnectionConfiguration
{
    /// <summary>租户 Id。</summary>
    public required Guid TenantId { get; init; }

    /// <summary>数据放置方式。</summary>
    public required TenantDatabaseMode DatabaseMode { get; init; }

    /// <summary>API 与后台 DML 使用的 Secret 引用。</summary>
    public string? RuntimeSecretReference { get; init; }

    /// <summary>DbMigrator 执行 DDL 使用的 Secret 引用。</summary>
    public string? MigrationSecretReference { get; init; }

    /// <summary>从 1 开始、每次修改递增的配置版本。</summary>
    public long Version { get; init; }

    /// <inheritdoc />
    public override string ToString() =>
        $"{nameof(TenantConnectionConfiguration)} {{ TenantId = {TenantId}, DatabaseMode = {DatabaseMode}, Version = {Version} }}";
}
