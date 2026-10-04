using Microsoft.Extensions.Logging;
using CompanyName.ProjectName.Application;
using CompanyName.ProjectName.Domain;
using CompanyName.ProjectName.Infrastructure;
using Leistd.Security;
using Leistd.Tracing;
#if (!IncludeOperationRecords)
using Leistd.OperationRecords.Logging;
#endif
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyName.ProjectName.DbMigrator;

internal static class ResourceAdminBootstrapServices
{
    public static IServiceCollection AddResourceAdminBootstrapServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAmbientContext();
        services.AddCorrelationIdCore();
        services.AddDomainServices();
        services.AddInfrastructureServices(configuration);
        services.AddApplicationServices();
#if (!IncludeOperationRecords)
        services.AddOperationRecordsLogging();
        services.AddLogging(logging => logging.AddFilter("Leistd.OperationRecords", Microsoft.Extensions.Logging.LogLevel.Information));
#endif
        services.AddScoped<ResourceAdminBootstrapRunner>();
        return services;
    }
}
