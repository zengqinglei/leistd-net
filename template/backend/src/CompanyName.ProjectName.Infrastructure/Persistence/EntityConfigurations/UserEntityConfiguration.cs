using CompanyName.ProjectName.Domain.Users.Entities;
using Leistd.Ddd.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace CompanyName.ProjectName.Infrastructure.Persistence.EntityConfigurations;

/// <summary>
/// User 表的数据库检查约束名
/// </summary>
/// <remarks>
/// 提成常量：约束名出现在实体配置、迁移与测试三处，字面量拼错的表现是"约束看起来在、其实没建"。
/// </remarks>
internal static class UserCheckConstraints
{
    /// <summary><c>IsSuperAdmin</c> 只能出现在宿主行（<c>TenantId IS NULL</c>）</summary>
    internal const string SuperAdminIsHostOnly = "CK_User_SuperAdminIsHostOnly";
}

/// <summary>
/// User 实体基础配置（始终包含）
/// </summary>
internal static class BaseEntityConfiguration
{
    internal static void ConfigureBaseEntities(this ModelBuilder builder)
    {
        builder.ConfigureUser();
    }

    private static void ConfigureUser(this ModelBuilder builder)
    {
        builder.Entity<User>(b =>
        {
            b.ConfigureByConvention();

            b.Property(e => e.Username).IsRequired().HasMaxLength(64);
            b.Property(e => e.Email).IsRequired().HasMaxLength(256);
            b.Property(e => e.Avatar).HasColumnType("text");
            b.Property(e => e.DisplayName).HasMaxLength(128);
            b.Property(e => e.IsSuperAdmin);

            // 宿主专属不变量：IsSuperAdmin 只能出现在宿主行（为什么见 CreateSuperAdminAsync）。
            // 领域服务已在创建时拒绝，这一道是最终完整性——数据修复脚本、批量导入、
            // 直接 SQL 都不经过领域入口。
            b.ToTable(t => t.HasCheckConstraint(
                UserCheckConstraints.SuperAdminIsHostOnly,
                $"NOT (\"{nameof(User.IsSuperAdmin)}\" AND \"{nameof(User.TenantId)}\" IS NOT NULL)"));

            // 租户内唯一：跨租户允许同名/同邮箱。可空 TenantId 直接进唯一索引时，
            // PostgreSQL/SQLite 均视 NULL 互不相等，宿主行会失去唯一性兜底，
            // 因此宿主行与租户行分别用带过滤的唯一索引收口
            b.HasIndex(e => e.Username)
                .IsUnique()
                .HasFilter($"\"{nameof(User.TenantId)}\" IS NULL");
            b.HasIndex(e => e.Email)
                .IsUnique()
                .HasFilter($"\"{nameof(User.TenantId)}\" IS NULL");

            b.HasIndex(e => new { e.TenantId, e.Username })
                .IsUnique()
                .HasFilter($"\"{nameof(User.TenantId)}\" IS NOT NULL");
            b.HasIndex(e => new { e.TenantId, e.Email })
                .IsUnique()
                .HasFilter($"\"{nameof(User.TenantId)}\" IS NOT NULL");
        });
    }
}
