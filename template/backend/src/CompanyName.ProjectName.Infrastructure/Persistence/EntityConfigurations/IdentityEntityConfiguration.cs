#if (LocalIdentity)
using CompanyName.ProjectName.Domain.Auth.Entities;
using CompanyName.ProjectName.Domain.Users.Entities;
using Leistd.Ddd.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace CompanyName.ProjectName.Infrastructure.Persistence.EntityConfigurations;

internal static class IdentityEntityConfiguration
{
    internal static void ConfigureIdentity(this ModelBuilder builder)
    {
        builder.ConfigureUserIdentity();
        builder.ConfigureRoles();
        builder.ConfigureUserRoles();
        builder.ConfigureUserSessions();
#if (ExternalLogin)
        builder.ConfigureExternalLoginConnections();
#endif
    }

    private static void ConfigureUserIdentity(this ModelBuilder builder)
    {
        builder.Entity<User>(b =>
        {
            b.Property(e => e.PasswordHash).HasMaxLength(256);
            b.Property(e => e.PhoneNumber).HasMaxLength(32);
            b.Property(e => e.LastLoginIp).HasMaxLength(45);
            b.Property(e => e.TwoFactorSecret).HasMaxLength(512);
            // 十个 SHA-256 十六进制摘要加分隔符
            b.Property(e => e.TwoFactorRecoveryCodes).HasMaxLength(1024);
        });
    }

    private static void ConfigureRoles(this ModelBuilder builder)
    {
        builder.Entity<Role>(b =>
        {
            b.ConfigureByConvention();

            b.Property(e => e.Name).IsRequired().HasMaxLength(64);
            b.Property(e => e.DisplayName).IsRequired().HasMaxLength(128);
            b.Property(e => e.Description).HasMaxLength(512);

            // 租户内唯一：每个租户拥有自己的 Admin/Member 角色。
            // 宿主行与租户行分别用带过滤的唯一索引（可空列直接进唯一索引时 NULL 互不相等）
            b.HasIndex(e => e.Name)
                .IsUnique()
                .HasFilter($"\"{nameof(Role.TenantId)}\" IS NULL");

            b.HasIndex(e => new { e.TenantId, e.Name })
                .IsUnique()
                .HasFilter($"\"{nameof(Role.TenantId)}\" IS NOT NULL");
        });
    }

    private static void ConfigureUserSessions(this ModelBuilder builder)
    {
        builder.Entity<UserSession>(b =>
        {
            b.ConfigureByConvention();

            b.Property(e => e.IpAddress).HasMaxLength(UserSession.IpAddressMaxLength);
            b.Property(e => e.UserAgent).HasMaxLength(UserSession.UserAgentMaxLength);
            b.Property(e => e.ImpersonatorName).HasMaxLength(UserSession.ImpersonatorNameMaxLength);

            b.HasIndex(e => e.UserId);

            // 会话没有独立于用户的意义：用户行被物理删除时一并删除
            b.HasOne<User>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }

#if (ExternalLogin)
    private static void ConfigureExternalLoginConnections(this ModelBuilder builder)
    {
        builder.Entity<ExternalLoginConnection>(b =>
        {
            b.ConfigureByConvention();

            b.Property(e => e.Provider).IsRequired().HasMaxLength(50);
            b.Property(e => e.ProviderUserId).IsRequired().HasMaxLength(256);
            b.Property(e => e.ProviderUsername).HasMaxLength(256);
            b.Property(e => e.ProviderEmail).HasMaxLength(256);
            b.Property(e => e.ProviderAvatarUrl).HasMaxLength(1024);
            b.Property(e => e.AccessToken).HasMaxLength(2048);
            b.Property(e => e.RefreshToken).HasMaxLength(2048);

            // 租户内唯一：同一外部身份可在不同租户各自绑定。
            // 宿主行（TenantId 为 NULL）在 PostgreSQL/SQLite 中 NULL 互不相等，
            // 用带过滤的成对索引分别约束，避免宿主侧失去唯一性兜底。
            // 只约束未删除的行：解绑是软删除，留着的旧行不能挡住同一外部账号重新绑定
            b.HasIndex(e => new { e.Provider, e.ProviderUserId })
                .IsUnique()
                .HasFilter($"\"{nameof(ExternalLoginConnection.TenantId)}\" IS NULL AND NOT \"{nameof(ExternalLoginConnection.IsDeleted)}\"");

            b.HasIndex(e => new { e.TenantId, e.Provider, e.ProviderUserId })
                .IsUnique()
                .HasFilter($"\"{nameof(ExternalLoginConnection.TenantId)}\" IS NOT NULL AND NOT \"{nameof(ExternalLoginConnection.IsDeleted)}\"");
            b.HasIndex(e => e.UserId);

            b.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
#endif

    private static void ConfigureUserRoles(this ModelBuilder builder)
    {
        builder.Entity<UserRole>(b =>
        {
            b.ConfigureByConvention();

            b.HasIndex(e => new { e.UserId, e.RoleId, e.DeletionTime }).IsUnique();
            b.HasIndex(e => e.RoleId);

            b.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict).IsRequired(false);
            b.HasOne(e => e.Role).WithMany().HasForeignKey(e => e.RoleId).OnDelete(DeleteBehavior.Restrict).IsRequired(false);
        });
    }

}
#endif
