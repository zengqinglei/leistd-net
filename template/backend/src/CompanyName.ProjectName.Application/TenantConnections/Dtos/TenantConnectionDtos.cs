#if (LocalIdentity)
using Leistd.MultiTenancy.ConnectionStrings;
using System.ComponentModel.DataAnnotations;
using Leistd.MultiTenancy;

namespace CompanyName.ProjectName.Application.TenantConnections.Dtos;

public sealed record TenantConnectionOutputDto
{
    public required Guid TenantId { get; init; }
    public required TenantDatabaseMode DatabaseMode { get; init; }
    public string? RuntimeSecretReference { get; init; }
    public string? MigrationSecretReference { get; init; }
    public required long Version { get; init; }
}

public sealed record TenantRuntimeConnectionOutputDto
{
    public required Guid TenantId { get; init; }
    public required TenantDatabaseMode DatabaseMode { get; init; }
    public string? RuntimeSecretReference { get; init; }
    public required long Version { get; init; }
}

public sealed record TenantMigrationConnectionOutputDto
{
    public required Guid TenantId { get; init; }
    public required TenantDatabaseMode DatabaseMode { get; init; }
    public string? MigrationSecretReference { get; init; }
    public required long Version { get; init; }
}

/// <summary>
/// 更新租户连接配置
/// </summary>
/// <remarks>
/// 实现 <see cref="IValidatableObject"/> 是因为"模式与 Secret 引用是否匹配"是跨字段规则，
/// 单个字段的注解表达不了。这条校验必须落在入口 DTO：框架的
/// <c>ITenantConnectionConfigurationManager</c> 对同一条件抛 <see cref="ArgumentException"/>，
/// 那是编程契约异常、不在 <c>Leistd.ExceptionHandling.Core</c> 家族里，
/// 会被全局兜底处理器转成 500 + "系统错误"，原始原因丢失、还产出 Error 级日志。
/// 客户端传错组合应当拿到 400。
/// </remarks>
public sealed record UpdateTenantConnectionInputDto : IValidatableObject
{
    public required TenantDatabaseMode DatabaseMode { get; init; }

    /// <summary>
    /// 调用方读到的版本；<see langword="null"/> 表示预期该配置尚不存在（首次配置）。
    /// </summary>
    /// <remarks>
    /// 声明为 <c>required</c> 而非可选：System.Text.Json 会对缺失的 required 成员报错，
    /// 于是"忘了带版本"表现为 400，而不是静默按后写者胜出处理。
    /// 显式传 <see langword="null"/> 与不传是两件不同的事，这里必须能区分。
    /// </remarks>
    public required long? ExpectedVersion { get; init; }

    [MaxLength(512)]
    public string? RuntimeSecretReference { get; init; }

    [MaxLength(512)]
    public string? MigrationSecretReference { get; init; }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        TenantConnectionInputValidator.Validate(
            DatabaseMode,
            RuntimeSecretReference,
            MigrationSecretReference,
            nameof(RuntimeSecretReference),
            nameof(MigrationSecretReference));
}
#endif
