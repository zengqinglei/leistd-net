#if (IdentityService)
using Leistd.MultiTenancy;
using Leistd.MultiTenancy.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CompanyName.ProjectName.Infrastructure.Persistence;

/// <summary>
/// Identity control-plane storage. This context is always pinned to the host database.
/// Tenant business data belongs to <see cref="MyProjectDbContext"/>.
/// </summary>
[TenantConnectionStringName(ConnectionStringName)]
public sealed class IdentityControlDbContext(DbContextOptions<IdentityControlDbContext> options)
    : DbContext(options)
{
    public const string ConnectionStringName = "IdentityControl";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("companyname-projectname");
        modelBuilder.ConfigureMultiTenancy();
    }
}
#endif
