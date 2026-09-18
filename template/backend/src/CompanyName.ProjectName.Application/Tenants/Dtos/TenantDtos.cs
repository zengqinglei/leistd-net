using System.ComponentModel.DataAnnotations;
using CompanyName.ProjectName.Domain.Users.Policies;
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
    /// 连这个匿名端点都到不了（见 <c>Subdomain_of_a_missing_tenant_is_rejected_during_resolution</c>）。
    /// 真的取不到时 <see cref="Decision"/> 仍是 <see cref="HostTenantDecision.Tenant"/>：
    /// 域名已经定了案，界面不该因此退回让用户自己挑一个——挑了也会被域名覆盖。
    /// </remarks>
    public TenantLookupOutputDto? Tenant { get; init; }
}

/// <summary>
/// 创建租户入参：同时提供租户管理员的初始凭据，创建后立即在租内种子
/// </summary>
public record CreateTenantInputDto
{
    [Display(Name = "Tenant name")]
    [Required(ErrorMessage = "{0} is required.")]
    [MaxLength(64, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public required string Name { get; init; }

    [Display(Name = "Display name")]
    [MaxLength(128, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public string? DisplayName { get; init; }

    /// <summary>简短描述，说明该租户的用途。</summary>
    [Display(Name = "Description")]
    [MaxLength(256, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public string? Description { get; init; }

    /// <summary>租户管理员邮箱</summary>
    [Display(Name = "Admin email")]
    [Required(ErrorMessage = "{0} is required.")]
    [EmailAddress(ErrorMessage = "{0} has an invalid format.")]
    [MaxLength(256, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public required string AdminEmail { get; init; }

    /// <summary>租户管理员初始密码</summary>
    [Display(Name = "Admin password")]
    [Required(ErrorMessage = "{0} is required.")]
    // 仅快速反馈；权威在服务端 PasswordPolicy
    [MinLength(PasswordPolicy.MinimumLength, ErrorMessage = "{0} must be at least {1} characters.")]
    [MaxLength(PasswordPolicy.MaximumLength, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public required string AdminPassword { get; init; }

    /// <summary>
    /// 该租户专属库的连接串；留空即不分库，各服务使用自己配置的数据库。
    /// </summary>
    /// <remarks>
    /// <para><b>分库只能在这里定案。</b>播种紧随登记之后，因此解析到的已经是这个库。
    /// 反过来先播种再登记连接，种子连同租户管理员会留在回落库里、新库是空的，租户当场登不上；
    /// 框架的 <c>ITenantConnectionConfigurationManager.SetAsync</c> 正因如此会拒绝
    /// 给一条登记都没有的<b>在用</b>租户登记第一条连接。</para>
    /// <para>库要<b>先建好并迁移过</b>（用 <c>ConnectionStrings:MigrationTarget</c> 预迁移一个尚未登记的目标）；
    /// 这里只登记，不建库也不迁移。登记到默认名下，即"一租户一库、各服务不同 schema"。
    /// 要把不同服务拆到不同库，再去租户的连接列表里逐条登记。</para>
    /// </remarks>
    [Display(Name = "Connection string")]
    [MaxLength(TenantConnectionConfiguration.MaxConnectionStringLength, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public string? ConnectionString { get; init; }

    /// <summary>不输出初始密码与连接串：记录类型默认打印全部属性，随手写进日志就是泄露。</summary>
    public override string ToString() =>
        $"{nameof(CreateTenantInputDto)} {{ Name = {Name}, DisplayName = {DisplayName}, AdminEmail = {AdminEmail} }}";
}

/// <summary>
/// 更新租户入参
/// </summary>
public record UpdateTenantInputDto
{
    [Display(Name = "Tenant name")]
    [Required(ErrorMessage = "{0} is required.")]
    [MaxLength(64, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public required string Name { get; init; }

    [Display(Name = "Display name")]
    [MaxLength(128, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public string? DisplayName { get; init; }

    /// <summary>简短描述；传 <c>null</c> 即清空。</summary>
    [Display(Name = "Description")]
    [MaxLength(256, ErrorMessage = "{0} cannot exceed {1} characters.")]
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
