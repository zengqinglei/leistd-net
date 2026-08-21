using CompanyName.ProjectName.DbMigrator;
using CompanyName.ProjectName.Domain;
using CompanyName.ProjectName.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddDomainServices();
builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddScoped<DatabaseMigrationRunner>();

try
{
    using var host = builder.Build();
    await using var scope = host.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<DatabaseMigrationRunner>().RunAsync();
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Database migration failed: {exception.GetType().Name}");
    return 1;
}
