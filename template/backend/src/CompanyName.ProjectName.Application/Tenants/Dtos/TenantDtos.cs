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
/// 创建租户入参：同时提供租户管理员的初始凭据，创建后立即在租内种子
/// </summary>
public record CreateTenantInputDto : IValidatableObject
{
    [Required]
    [MaxLength(64)]
    public required string Name { get; init; }

    [MaxLength(128)]
    public string? DisplayName { get; init; }

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
}

/// <summary>
/// 租户启停入参
/// </summary>
public record UpdateTenantActivationInputDto
{
    /// <summary>停用后该租户的请求自下一次校验起被拒绝（403）</summary>
    public required bool IsActive { get; init; }
}
