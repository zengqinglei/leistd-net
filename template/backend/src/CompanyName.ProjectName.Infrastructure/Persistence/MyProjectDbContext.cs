using CompanyName.ProjectName.Domain.Users.Entities;
#if (IncludeExternalLogin)
using CompanyName.ProjectName.Domain.Auth.Entities;
#endif
#if (LocalAuthorization)
using Leistd.Authorization.EntityFrameworkCore;
#endif
using Leistd.Ddd.Infrastructure.Persistence;
#if (IncludeNotifications)
using Leistd.Notifications.EntityFrameworkCore;
#endif
using Microsoft.EntityFrameworkCore;
using CompanyName.ProjectName.Infrastructure.Persistence.EntityConfigurations;

namespace CompanyName.ProjectName.Infrastructure.Persistence;

public class MyProjectDbContext(
    DbContextOptions<MyProjectDbContext> options,
    IServiceProvider? serviceProvider) : BaseDbContext(options, serviceProvider)
{
    // Users（始终存在）
    public DbSet<User> Users { get; set; } = null!;
#if (LocalAuthorization)
    // Identity 角色模型
    public DbSet<Role> Roles { get; set; } = null!;
    public DbSet<UserRole> UserRoles { get; set; } = null!;
#endif
#if (IncludeExternalLogin)
    public DbSet<ExternalLoginConnection> ExternalLoginConnections { get; set; } = null!;
#endif
#if (LocalAuthorization)
    public DbSet<PermissionGrantRecord> PermissionGrantRecords { get; set; } = null!;
#endif

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(64);
    }

    /// <remarks>
    /// 改写 <c>ConfigureModel</c> 而非 <c>OnModelCreating</c>：基类已封闭后者，
    /// 以保证软删除与租户全局过滤器在本方法之后套用，覆盖这里经
    /// <c>Configure*</c> 才进入模型的实体（它们没有 DbSet 声明）。
    /// </remarks>
    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("companyname-projectname");

        // 基础实体配置（始终包含）
        modelBuilder.ConfigureBaseEntities();
#if (IdentityService)
        // 认证相关实体配置
        modelBuilder.ConfigureIdentity();
#endif
#if (LocalAuthorization)
        // 权限授予实体配置
        modelBuilder.ConfigureAuthorization();
#endif
#if (IncludeNotifications)
        // 通知实体配置
        modelBuilder.ConfigureNotifications();
#endif
    }
}
