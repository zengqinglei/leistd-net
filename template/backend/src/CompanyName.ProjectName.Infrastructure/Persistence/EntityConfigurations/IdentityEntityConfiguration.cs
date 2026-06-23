#if (IncludeIdentity)
#if (IncludeRoles)
using CompanyName.ProjectName.Domain.Permissions.Entities;
#endif
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
        builder.ConfigureExternalLoginConnections();
#if (IncludeRoles)
        builder.ConfigurePermissionGrants();
#endif
    }

    private static void ConfigureUserIdentity(this ModelBuilder builder)
    {
        builder.Entity<User>(b =>
        {
            b.Property(e => e.PasswordHash).HasMaxLength(256);
            b.Property(e => e.PhoneNumber).HasMaxLength(32);
            b.Property(e => e.LastLoginIp).HasMaxLength(45);
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

            b.HasIndex(e => e.Name).IsUnique();
        });
    }

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

            b.HasIndex(e => new { e.Provider, e.ProviderUserId }).IsUnique();
            b.HasIndex(e => e.UserId);

            b.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
        });
    }

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

#if (IncludeRoles)
    private static void ConfigurePermissionGrants(this ModelBuilder builder)
    {
        builder.Entity<PermissionGrant>(b =>
        {
            b.ConfigureByConvention();

            b.Property(e => e.PermissionName).IsRequired().HasMaxLength(256);
            b.Property(e => e.ProviderName).IsRequired().HasMaxLength(32);
            b.Property(e => e.ProviderKey).IsRequired().HasMaxLength(128);

            b.HasIndex(e => new { e.PermissionName, e.ProviderName, e.ProviderKey }).IsUnique();
            b.HasIndex(e => new { e.ProviderName, e.ProviderKey });
        });
    }
#endif
}
#endif
