#if (IdentityService)
using CompanyName.ProjectName.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using OpenIddict.EntityFrameworkCore;

namespace CompanyName.ProjectName.Api;

public sealed class IdentityControlDbContextFactory : IDesignTimeDbContextFactory<IdentityControlDbContext>
{
    private const string PlaceholderConnectionString =
        "Host=localhost;Port=5432;Database=companyname_projectname_design;Username=postgres;Password=postgres";

    public IdentityControlDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString(IdentityControlDbContext.ConnectionStringName)
            ?? configuration.GetConnectionString("Default")
            ?? PlaceholderConnectionString;
        var options = new DbContextOptionsBuilder<IdentityControlDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory_Control", "companyname-projectname"))
            .UseOpenIddict()
            .Options;

        return new IdentityControlDbContext(options);
    }
}
#endif
