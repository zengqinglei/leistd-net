using System.ComponentModel.DataAnnotations;
using Leistd.Data.Paging;
using Leistd.MultiTenancy.ConnectionStrings;

namespace Leistd.MultiTenancy.Dtos;

/// <summary>租户分页查询入参。</summary>
public record GetTenantPagedInputDto : PageRequest
{
    /// <summary>名称或显示名关键字（大小写不敏感）。</summary>
    public string? Keyword { get; init; }
}

/// <summary>租户输出。</summary>
public record TenantOutputDto
{
    /// <summary>租户标识。</summary>
    public required Guid Id { get; init; }

    /// <summary>大小写不敏感的唯一名称。</summary>
    public required string Name { get; init; }

    /// <summary>显示名。</summary>
    public string? DisplayName { get; init; }

    /// <summary>简短描述。</summary>
    public string? Description { get; init; }

    /// <summary>是否启用。</summary>
    public required bool IsActive { get; init; }

    /// <summary>创建时间。</summary>
    public required DateTime CreationTime { get; init; }
}

/// <summary>登录前租户探测输出（匿名端点，只暴露选择租户所需的最小信息）。</summary>
public record TenantLookupOutputDto
{
    /// <summary>租户标识。</summary>
    public required Guid Id { get; init; }

    /// <summary>名称。</summary>
    public required string Name { get; init; }

    /// <summary>显示名。</summary>
    public string? DisplayName { get; init; }

    /// <summary>是否启用。</summary>
    public required bool IsActive { get; init; }
}

/// <summary>
/// 域名对租户的定案结果。
/// </summary>
/// <remarks>
/// 三档必须分开：<see cref="Host"/> 是域名已经定了案（宿主），<see cref="Undecided"/> 是域名不表态、后续解析来源仍可决定。
/// 把两者都讲成"没有租户"，登录页会在宿主域上继续显示上次记住的租户，而服务端已按宿主处理请求。
/// </remarks>
public enum HostTenantDecision
{
    /// <summary>主机名不在受管域内，或未配置域名格式：域名不参与定案。</summary>
    Undecided,

    /// <summary>受管域内但未指向任何租户：定案为宿主。</summary>
    Host,

    /// <summary>受管域内的租户子域：定案为该租户。</summary>
    Tenant
}

/// <summary>按主机名解析租户的结果。</summary>
public record TenantByHostOutputDto
{
    /// <summary>定案结果。</summary>
    public required HostTenantDecision Decision { get; init; }

    /// <summary>定案到的租户，只有 <see cref="HostTenantDecision.Tenant"/> 时才可能有值。</summary>
    public TenantLookupOutputDto? Tenant { get; init; }
}

/// <summary>
/// 创建租户入参。宿主要在开通时收集更多信息（如租户管理员），就派生这个类型并交给端点与开通器。
/// </summary>
public record CreateTenantInputDto
{
    /// <summary>租户名称。</summary>
    [Required]
    [MaxLength(64)]
    public required string Name { get; init; }

    /// <summary>显示名。</summary>
    [MaxLength(128)]
    public string? DisplayName { get; init; }

    /// <summary>简短描述。</summary>
    [MaxLength(256)]
    public string? Description { get; init; }

    /// <summary>
    /// 该租户的专属库连接，按名字登记；留空即不分库，各服务用自己配置的库。
    /// </summary>
    /// <remarks>
    /// <para>分库只能在创建时定案：开通紧随登记之后，解析到的已经是这些库。库要先建好并迁移过，这里只登记。</para>
    /// <para>多服务部署可以一次登记多条（如 <c>default</c>、<c>crm</c>）：租户与全部连接在同一个控制面工作单元里落库，
    /// 不会出现"租户已建、某条连接还没登记"的中间状态，开通钩子第一次执行时看到的就是完整的连接集合。</para>
    /// </remarks>
    /// <remarks>请求体显式传 <c>null</c> 时按"没给"处理：默认值挡不住显式 null，而下游按非空用它。</remarks>
    public IReadOnlyList<CreateTenantConnectionInputDto> Connections
    {
        get => _connections;
        init => _connections = value ?? [];
    }

    private readonly IReadOnlyList<CreateTenantConnectionInputDto> _connections = [];

    /// <summary>不输出连接串。</summary>
    public override string ToString() =>
        $"{nameof(CreateTenantInputDto)} {{ Name = {Name}, DisplayName = {DisplayName}, Connections = {Connections.Count} }}";
}

/// <summary>
/// 创建租户时登记的一条命名连接。
/// </summary>
/// <remarks>名字与连接串在任何库操作之前统一校验：名字按 <c>^[a-z0-9-]{1,64}$</c> 归一化，重名与语法错误都在写库前拒绝。</remarks>
public record CreateTenantConnectionInputDto
{
    /// <summary>连接名，对应使用方 DbContext 的 <c>[ConnectionStringName]</c>；不区分大小写。</summary>
    [Required]
    [MaxLength(TenantConnectionConfiguration.MaxNameLength)]
    public required string Name { get; init; }

    /// <summary>连接串。</summary>
    [Required]
    [MaxLength(TenantConnectionConfiguration.MaxConnectionStringLength)]
    public required string ConnectionString { get; init; }

    /// <summary>不输出连接串。</summary>
    public override string ToString() => $"{nameof(CreateTenantConnectionInputDto)} {{ Name = {Name} }}";
}

/// <summary>更新租户入参；三个字段都按传入值覆盖。</summary>
public record UpdateTenantInputDto
{
    /// <summary>租户名称。</summary>
    [Required]
    [MaxLength(64)]
    public required string Name { get; init; }

    /// <summary>显示名；传 <see langword="null"/> 即清空。</summary>
    [MaxLength(128)]
    public string? DisplayName { get; init; }

    /// <summary>简短描述；传 <see langword="null"/> 即清空。</summary>
    [MaxLength(256)]
    public string? Description { get; init; }
}

/// <summary>租户启停入参。</summary>
public record UpdateTenantActivationInputDto
{
    /// <summary>停用后该租户的请求自下一次校验起被拒绝。</summary>
    public required bool IsActive { get; init; }
}
