using System.ComponentModel.DataAnnotations;
using Leistd.MultiTenancy.ConnectionStrings;

namespace Leistd.MultiTenancy.Dtos;

/// <summary>
/// 租户的一条连接登记（管理面）。
/// </summary>
/// <remarks>连接串只写不读：管理面只回名字与版本。租户一条登记都没有，就表示它不单独分库。</remarks>
public sealed record TenantConnectionOutputDto
{
    /// <summary>租户标识。</summary>
    public required Guid TenantId { get; init; }

    /// <summary>归一化后的连接名。</summary>
    public required string Name { get; init; }

    /// <summary>该行的乐观并发版本。</summary>
    public required long Version { get; init; }
}

/// <summary>
/// 下发给资源服务的运行时查询结果，形态与 <see cref="TenantConnectionLookupResult"/> 一致。
/// </summary>
/// <remarks>
/// 控制面端点与远端连接存储共用这一份线上契约：两边各写一份，改一边另一边就静默解析失败。
/// </remarks>
public sealed record TenantRuntimeConnectionOutputDto
{
    /// <summary>被查询的租户。</summary>
    public required Guid TenantId { get; init; }

    /// <summary>该租户是否登记过任意连接；<see langword="false"/> 表示不分库，调用方用自己的配置。</summary>
    public required bool HasAnyConnection { get; init; }

    /// <summary>命中的连接；登记过却为空表示缺这个名字，调用方必须失败关闭。</summary>
    public TenantConnectionDetailOutputDto? Connection { get; init; }

    /// <summary>不输出连接串。</summary>
    public override string ToString() =>
        $"{nameof(TenantRuntimeConnectionOutputDto)} {{ TenantId = {TenantId}, " +
        $"HasAnyConnection = {HasAnyConnection}, Name = {Connection?.Name ?? "<none>"} }}";
}

/// <summary>命中的那一条连接，<see cref="ConnectionString"/> 为解密后的明文。</summary>
public sealed record TenantConnectionDetailOutputDto
{
    /// <summary>实际命中的连接名（精确名或默认名）。</summary>
    public required string Name { get; init; }

    /// <summary>连接串明文；不写进日志。</summary>
    public required string ConnectionString { get; init; }

    /// <summary>该行的版本。</summary>
    public required long Version { get; init; }

    /// <summary>不输出连接串：记录类型默认打印全部属性，随手写进日志就是泄露。</summary>
    public override string ToString() =>
        $"{nameof(TenantConnectionDetailOutputDto)} {{ Name = {Name}, Version = {Version} }}";
}

/// <summary>下发给迁移作业的一条连接，<see cref="ConnectionString"/> 为解密后的明文。</summary>
public sealed record TenantMigrationConnectionOutputDto
{
    /// <summary>租户标识。</summary>
    public required Guid TenantId { get; init; }

    /// <summary>实际命中的连接名，用于回答"为什么迁到了这个库"。</summary>
    public required string Name { get; init; }

    /// <summary>连接串明文；不写进日志。</summary>
    public required string ConnectionString { get; init; }

    /// <summary>不输出连接串。</summary>
    public override string ToString() =>
        $"{nameof(TenantMigrationConnectionOutputDto)} {{ TenantId = {TenantId}, Name = {Name} }}";
}

/// <summary>
/// 登记或更新一条租户连接；连接名走路由。
/// </summary>
/// <remarks>想让某个名字回到"用服务自己的库"，删掉这一条，而不是提交空连接串。</remarks>
public sealed record UpsertTenantConnectionInputDto
{
    /// <summary>
    /// 调用方读到的该行版本；<see langword="null"/> 表示预期这一行尚不存在（首次登记）。
    /// </summary>
    /// <remarks>声明为 <c>required</c>：缺失时反序列化报错，"忘了带版本"表现为 400，而不是静默按后写者胜出处理。</remarks>
    public required long? ExpectedVersion { get; init; }

    /// <summary>连接串（明文）；加密后存入控制库，运行与迁移共用。</summary>
    [Required]
    [MaxLength(TenantConnectionConfiguration.MaxConnectionStringLength)]
    public required string ConnectionString { get; init; }

    /// <summary>不输出连接串。</summary>
    public override string ToString() =>
        $"{nameof(UpsertTenantConnectionInputDto)} {{ ExpectedVersion = {ExpectedVersion} }}";
}

/// <summary>
/// 一个独立库，以及住在里面的租户。
/// </summary>
/// <remarks>
/// 逐库作业用的线上形状：<b>不含连接串</b>。真正的连接由各租户的正常解析链取得，
/// 因此这个端点只要"读路由"这一档权限，不需要迁移用的 DDL 身份。
/// </remarks>
public sealed record TenantDatabaseOutputDto
{
    /// <summary>连接串指纹，用于判定"是不是同一个库"；不可逆推连接串。</summary>
    public required string Fingerprint { get; init; }

    /// <summary>住在这个库里的租户。</summary>
    public required IReadOnlyList<Guid> TenantIds { get; init; }
}

/// <summary>
/// 一个解析不出连接的租户。
/// </summary>
/// <remarks>坏掉一个租户不该让整轮逐库作业不执行，因此它们与清单一起下发，由调用方记日志。</remarks>
public sealed record TenantDatabaseFailureOutputDto
{
    /// <summary>租户标识。</summary>
    public required Guid TenantId { get; init; }

    /// <summary>诊断消息；不含连接串。</summary>
    public required string Reason { get; init; }
}

/// <summary>逐库作业的库清单。</summary>
public sealed record TenantDatabaseListOutputDto
{
    /// <summary>独立库。</summary>
    public required IReadOnlyList<TenantDatabaseOutputDto> Databases { get; init; }

    /// <summary>解析不出连接、本轮被跳过的租户。</summary>
    public required IReadOnlyList<TenantDatabaseFailureOutputDto> FailedTenants { get; init; }
}
