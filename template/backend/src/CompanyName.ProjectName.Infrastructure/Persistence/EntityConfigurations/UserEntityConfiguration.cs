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

#if (IncludeTenancy)
            // 租户内唯一：跨租户允许同名/同邮箱。宿主行（TenantId 为 NULL）在 PostgreSQL 上
            // NULL 互不相等，其唯一性由 UserDomainService 的可用性校验兜底
            b.HasIndex(e => new { e.TenantId, e.Username }).IsUnique();
            b.HasIndex(e => new { e.TenantId, e.Email }).IsUnique();
#else
            b.HasIndex(e => e.Username).IsUnique();
            b.HasIndex(e => e.Email).IsUnique();
#endif
        });
    }
}
