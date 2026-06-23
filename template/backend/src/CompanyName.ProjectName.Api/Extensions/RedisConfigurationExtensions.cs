using CompanyName.ProjectName.Infrastructure.Shared.Redis;
using Microsoft.AspNetCore.DataProtection;

namespace CompanyName.ProjectName.Api.Extensions;

public static class RedisConfigurationExtensions
{
    public static IServiceCollection AddMyProjectDataProtection(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var redisConnStr = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrEmpty(redisConnStr))
        {
            var redisConfig = RedisConnectionStringParser.Parse(redisConnStr);
            services.AddDataProtection()
                .SetApplicationName("MyProject")
                .PersistKeysToStackExchangeRedis(
                    StackExchange.Redis.ConnectionMultiplexer.Connect(redisConfig),
                    "DataProtection-Keys");
        }
        else
        {
            var keysPath = Path.Combine(environment.ContentRootPath, "DataProtection-Keys");
            Directory.CreateDirectory(keysPath);
            services.AddDataProtection()
                .SetApplicationName("MyProject")
                .PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        }

        return services;
    }
}
