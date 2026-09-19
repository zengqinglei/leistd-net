#if (LocalIdentity)
namespace CompanyName.ProjectName.Client.Dtos;

/// <summary>
/// 按连接名查询租户连接的结果。
/// </summary>
/// <remarks>
/// <see cref="HasAnyConnection"/> 为 <see langword="false"/> 表示该租户不单独分库，调用方用自己的配置；
/// 为 <see langword="true"/> 但 <see cref="Connection"/> 为空表示它是分库租户却缺这个名字，调用方必须失败关闭。
/// </remarks>
public sealed record TenantConnectionLookupDto
{
    public required Guid TenantId { get; init; }
    public required bool HasAnyConnection { get; init; }
    public TenantConnectionDetailDto? Connection { get; init; }

    /// <summary>不输出连接串。</summary>
    public override string ToString() =>
        $"{nameof(TenantConnectionLookupDto)} {{ TenantId = {TenantId}, " +
        $"HasAnyConnection = {HasAnyConnection}, Name = {Connection?.Name ?? "<none>"} }}";
}

/// <summary>命中的那一条连接；<see cref="ConnectionString"/> 为 Identity 解密后下发的明文。</summary>
public sealed record TenantConnectionDetailDto
{
    public required string Name { get; init; }
    public required string ConnectionString { get; init; }
    public required long Version { get; init; }

    /// <summary>不输出连接串。</summary>
    public override string ToString() =>
        $"{nameof(TenantConnectionDetailDto)} {{ Name = {Name}, Version = {Version} }}";
}

/// <summary>迁移作业用的一条连接；与运行时共用同一条连接串。</summary>
public sealed record TenantMigrationConnectionDto
{
    public required Guid TenantId { get; init; }
    public required string Name { get; init; }
    public required string ConnectionString { get; init; }

    /// <summary>不输出连接串。</summary>
    public override string ToString() =>
        $"{nameof(TenantMigrationConnectionDto)} {{ TenantId = {TenantId}, Name = {Name} }}";
}
#endif
