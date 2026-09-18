#if (LocalIdentity)
using System.ComponentModel.DataAnnotations;
using Leistd.MultiTenancy.ConnectionStrings;

namespace CompanyName.ProjectName.Application.TenantConnections.Dtos;

/// <summary>
/// 租户的一条连接登记（管理面）
/// </summary>
/// <remarks>
/// 连接串只写不读：管理面只回名字与版本，明文只经已认证的内部接口下发给需要连库的服务。
/// 租户一条登记都没有，就表示它不单独分库、各服务使用自己配置的数据库。
/// </remarks>
public sealed record TenantConnectionOutputDto
{
    public required Guid TenantId { get; init; }
    public required string Name { get; init; }
    public required long Version { get; init; }
}

/// <summary>
/// 下发给资源服务的运行时查询结果
/// </summary>
/// <remarks>
/// 形态与框架的 <c>TenantConnectionLookupResult</c> 一致：
/// <see cref="HasAnyConnection"/> 为 <see langword="false"/> 表示该租户不分库，调用方用自己的配置；
/// 为 <see langword="true"/> 但 <see cref="Connection"/> 为空表示它是分库租户却缺这个名字，调用方必须失败关闭。
/// </remarks>
public sealed record TenantRuntimeConnectionOutputDto
{
    public required Guid TenantId { get; init; }
    public required bool HasAnyConnection { get; init; }
    public TenantConnectionDetailOutputDto? Connection { get; init; }

    /// <summary>不输出连接串。</summary>
    public override string ToString() =>
        $"{nameof(TenantRuntimeConnectionOutputDto)} {{ TenantId = {TenantId}, " +
        $"HasAnyConnection = {HasAnyConnection}, Name = {Connection?.Name ?? "<none>"} }}";
}

/// <summary>命中的那一条连接，<see cref="ConnectionString"/> 为解密后的明文。</summary>
public sealed record TenantConnectionDetailOutputDto
{
    public required string Name { get; init; }
    public required string ConnectionString { get; init; }
    public required long Version { get; init; }

    /// <summary>不输出连接串：记录类型默认打印全部属性，随手写进日志就是泄露。</summary>
    public override string ToString() =>
        $"{nameof(TenantConnectionDetailOutputDto)} {{ Name = {Name}, Version = {Version} }}";
}

/// <summary>
/// 下发给迁移作业的一条连接，<see cref="ConnectionString"/> 为解密后的明文
/// </summary>
/// <remarks><see cref="Name"/> 是实际命中的名字（精确名或默认名回落），用于回答"为什么迁到了这个库"。</remarks>
public sealed record TenantMigrationConnectionOutputDto
{
    public required Guid TenantId { get; init; }
    public required string Name { get; init; }
    public required string ConnectionString { get; init; }

    /// <summary>不输出连接串。</summary>
    public override string ToString() =>
        $"{nameof(TenantMigrationConnectionOutputDto)} {{ TenantId = {TenantId}, Name = {Name} }}";
}

/// <summary>
/// 登记或更新一条租户连接
/// </summary>
/// <remarks>
/// 连接名走路由，不在请求体里；名字的合法形态由框架归一化后校验。
/// 想让某个名字回到"用服务自己的库"，删掉这一条，而不是提交空连接串。
/// </remarks>
public sealed record UpsertTenantConnectionInputDto
{
    /// <summary>
    /// 调用方读到的该行版本；<see langword="null"/> 表示预期这一行尚不存在（首次登记）。
    /// </summary>
    /// <remarks>
    /// 声明为 <c>required</c> 而非可选：System.Text.Json 会对缺失的 required 成员报错，
    /// 于是"忘了带版本"表现为 400，而不是静默按后写者胜出处理。
    /// 显式传 <see langword="null"/> 与不传是两件不同的事，这里必须能区分。
    /// </remarks>
    public required long? ExpectedVersion { get; init; }

    /// <summary>连接串（明文）；加密后存入控制库，运行与迁移共用。</summary>
    [Display(Name = "Connection string")]
    [Required(ErrorMessage = "{0} is required.")]
    [MaxLength(TenantConnectionConfiguration.MaxConnectionStringLength, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public required string ConnectionString { get; init; }

    /// <summary>不输出连接串。</summary>
    public override string ToString() =>
        $"{nameof(UpsertTenantConnectionInputDto)} {{ ExpectedVersion = {ExpectedVersion} }}";
}
#endif
