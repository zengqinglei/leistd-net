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
            b.Property(e => e.Nickname).HasMaxLength(128);
            b.Property(e => e.IsSuperAdmin);

            b.HasIndex(e => e.Username).IsUnique();
            b.HasIndex(e => e.Email).IsUnique();
        });
    }
}
