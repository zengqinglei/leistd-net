using System.ComponentModel.DataAnnotations;
using CompanyName.ProjectName.Application.TenantConnections;
using CompanyName.ProjectName.Domain.Users.Passwords;
using Leistd.Ddd.Application.Contracts.Dtos;
using Leistd.MultiTenancy;
using Leistd.MultiTenancy.ConnectionStrings;

namespace CompanyName.ProjectName.Application.Tenants.Dtos;

/// <summary>
/// 租户分页查询入参
/// </summary>
public record GetTenantPagedInputDto : PagedRequestDto
{
    /// <summary>名称/显示名关键字（大小写不敏感）</summary>
    public string? Keyword { get; init; }
}

/// <summary>
/// 租户输出
/// </summary>
public record TenantOutputDto : EntityDto
{
    public required string Name { get; init; }

    public string? DisplayName { get; init; }

    /// <summary>简短描述，说明该租户的用途。</summary>
    public string? Description { get; init; }

    public required bool IsActive { get; init; }

    public required DateTime CreationTime { get; init; }
}

/// <summary>
/// 登录前租户探测输出（匿名端点，只暴露选择租户所需的最小信息）
/// </summary>
public record TenantLookupOutputDto : EntityDto
{
    public required string Name { get; init; }

    public string? DisplayName { get; init; }

    public required bool IsActive { get; init; }
}

/// <summary>
/// 域名对租户的定案结果
/// </summary>
/// <remarks>
/// 三档必须分开，不能合成"有没有租户"两档：<see cref="Host"/> 是域名已经定了案（宿主），
/// <see cref="Undecided"/> 是域名不表态、后续解析来源仍可决定。把两者都讲成"没有租户"，
/// 登录页会在宿主域上继续显示上次记住的租户，而服务端已按宿主处理请求——
/// 界面与实际生效的租户上下文对不上。
/// </remarks>
public enum HostTenantDecision
{
    /// <summary>主机名不在受管域内，或未配置 <c>DomainFormat</c>：域名不参与定案。</summary>
    Undecided,

    /// <summary>受管域内但未指向任何租户：定案为宿主，请求头不再能改写。</summary>
    Host,

    /// <summary>受管域内的租户子域：定案为该租户。</summary>
    Tenant
}

/// <summary>
/// 按主机名解析租户的结果
/// </summary>
public record TenantByHostOutputDto
{
    /// <summary>定案结果。</summary>
    public required HostTenantDecision Decision { get; init; }

    /// <summary>
    /// 定案到的租户，只有 <see cref="HostTenantDecision.Tenant"/> 时才可能有值。
    /// </summary>
    /// <remarks>
    /// 可空是因为契约不能承诺它非空（解析放行后租户被删掉这类窄窗口），
    /// 而不是"子域名写错了"那种情况——那种请求在<b>租户解析阶段</b>就被拒了，
    /// 连这个匿名端点都到不了（见 <c>子域名指向不存在的租户时请求在解析阶段被拒</c>）。
    /// 真的取不到时 <see cref="Decision"/> 仍是 <see cref="HostTenantDecision.Tenant"/>：
    /// 域名已经定了案，界面不该因此退回让用户自己挑一个——挑了也会被域名覆盖。
    /// </remarks>
    public TenantLookupOutputDto? Tenant { get; init; }
}

/// <summary>
/// 创建租户入参：同时提供租户管理员的初始凭据，创建后立即在租内种子
/// </summary>
public record CreateTenantInputDto : IValidatableObject
{
    [Required]
    [MaxLength(64)]
    public required string Name { get; init; }

    [MaxLength(128)]
    public string? DisplayName { get; init; }

    /// <summary>简短描述，说明该租户的用途。</summary>
    [MaxLength(256)]
    public string? Description { get; init; }

    /// <summary>租户管理员邮箱</summary>
    [Required]
    [EmailAddress]
    [MaxLength(256)]
    public required string AdminEmail { get; init; }

    /// <summary>租户管理员初始密码</summary>
    [Required]
    // 仅快速反馈；权威在服务端 PasswordPolicy
    [MinLength(PasswordPolicy.MinimumLength)]
    [MaxLength(PasswordPolicy.MaximumLength)]
    public required string AdminPassword { get; init; }

    /// <summary>默认共享数据库；选择独立数据库时必须同时提供两个 Secret 引用。</summary>
    public TenantDatabaseMode DatabaseMode { get; init; } = TenantDatabaseMode.SharedDatabase;

    [MaxLength(512)]
    public string? RuntimeSecretReference { get; init; }

    [MaxLength(512)]
    public string? MigrationSecretReference { get; init; }

    /// <inheritdoc />
    /// <remarks>
    /// 单字段注解表达不了"模式与 Secret 引用是否匹配"这条跨字段规则。判据与更新连接配置
    /// 共用同一个实现，见 <see cref="TenantConnectionInputValidator"/>。
    /// </remarks>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        TenantConnectionInputValidator.Validate(
            DatabaseMode,
            RuntimeSecretReference,
            MigrationSecretReference,
            nameof(RuntimeSecretReference),
            nameof(MigrationSecretReference));
}

/// <summary>
/// 更新租户入参
/// </summary>
public record UpdateTenantInputDto
{
    [Required]
    [MaxLength(64)]
    public required string Name { get; init; }

    [MaxLength(128)]
    public string? DisplayName { get; init; }

    /// <summary>简短描述；传 <c>null</c> 即清空。</summary>
    [MaxLength(256)]
    public string? Description { get; init; }
}

/// <summary>
/// 租户启停入参
/// </summary>
public record UpdateTenantActivationInputDto
{
    /// <summary>停用后该租户的请求自下一次校验起被拒绝（403）</summary>
    public required bool IsActive { get; init; }
}
