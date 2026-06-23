using CompanyName.ProjectName.Domain.Users.Entities;
#if (IncludeRoles)
using CompanyName.ProjectName.Domain.Permissions.Entities;
#endif
using Leistd.Ddd.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using CompanyName.ProjectName.Infrastructure.Persistence.EntityConfigurations;

namespace CompanyName.ProjectName.Infrastructure.Persistence;

public class MyProjectDbContext(
    DbContextOptions<MyProjectDbContext> options,
    IServiceProvider? serviceProvider) : BaseDbContext(options, serviceProvider)
{
    // Users（始终存在）
    public DbSet<User> Users { get; set; } = null!;
#if (IncludeIdentity)
    // Identity（认证模块）
    public DbSet<Role> Roles { get; set; } = null!;
    public DbSet<UserRole> UserRoles { get; set; } = null!;
#endif
#if (IncludeRoles)
    public DbSet<PermissionGrant> PermissionGrants { get; set; } = null!;
#endif

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(64);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // 基础实体配置（始终包含）
        modelBuilder.ConfigureBaseEntities();
#if (IncludeIdentity)
        // 认证相关实体配置
        modelBuilder.ConfigureIdentity();
#endif
    }
}
