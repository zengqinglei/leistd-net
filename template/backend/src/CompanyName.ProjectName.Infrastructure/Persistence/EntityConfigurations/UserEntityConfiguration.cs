using CompanyName.ProjectName.Domain.Users.Entities;
using Leistd.Ddd.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace CompanyName.ProjectName.Infrastructure.Persistence.EntityConfigurations;

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

#if (MultiTenancy)
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
#else
            b.HasIndex(e => e.Username).IsUnique();
            b.HasIndex(e => e.Email).IsUnique();
#endif
        });
    }
}
