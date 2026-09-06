#if (LocalIdentity)
using System.ComponentModel.DataAnnotations;
using Leistd.MultiTenancy.ConnectionStrings;

namespace CompanyName.ProjectName.Application.TenantConnections;

/// <summary>
/// "数据放置方式与 Secret 引用是否匹配"这条跨字段规则的<b>唯一</b>实现
/// </summary>
/// <remarks>
/// 创建租户与更新连接配置共用此 DTO 边界校验，使无效组合返回 400 并定位到字段。
/// 框架管理器仍校验直接调用的编程契约，数据库检查约束则覆盖脚本和直接 SQL；
/// 三层分别保护各自的信任边界。
/// </remarks>
internal static class TenantConnectionInputValidator
{
    /// <summary>
    /// 校验模式与两个 Secret 引用的组合
    /// </summary>
    /// <param name="databaseMode">数据放置方式</param>
    /// <param name="runtimeSecretReference">运行时 Secret 引用</param>
    /// <param name="migrationSecretReference">迁移 Secret 引用</param>
    /// <param name="runtimeMemberName">运行时引用在入口 DTO 上的属性名（用于定位错误字段）</param>
    /// <param name="migrationMemberName">迁移引用在入口 DTO 上的属性名</param>
    internal static IEnumerable<ValidationResult> Validate(
        TenantDatabaseMode databaseMode,
        string? runtimeSecretReference,
        string? migrationSecretReference,
        string runtimeMemberName,
        string migrationMemberName)
    {
        string[] secretMembers = [runtimeMemberName, migrationMemberName];

        switch (databaseMode)
        {
            case TenantDatabaseMode.SharedDatabase
                when runtimeSecretReference is not null || migrationSecretReference is not null:
                yield return new ValidationResult(
                    "Shared database configuration cannot contain Secret references.",
                    secretMembers);
                break;

            case TenantDatabaseMode.DedicatedDatabase
                when string.IsNullOrWhiteSpace(runtimeSecretReference) ||
                     string.IsNullOrWhiteSpace(migrationSecretReference):
                yield return new ValidationResult(
                    "Dedicated database configuration requires runtime and migration Secret references.",
                    secretMembers);
                break;
        }
    }
}
#endif
