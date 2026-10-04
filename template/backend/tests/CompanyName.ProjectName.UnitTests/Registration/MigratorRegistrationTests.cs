using CompanyName.ProjectName.DbMigrator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace CompanyName.ProjectName.UnitTests.Registration;

/// <summary>
/// 迁移作业的注册面：调用 DbMigrator 的同一组合，按开发环境的方式构建容器。
/// </summary>
/// <remarks>
/// 开发环境的宿主在构建期校验每条注册的依赖（<c>ValidateOnBuild</c>）。迁移作业只注册持久化；
/// 运行期组件依赖只在 API 里注册的当前用户与权限主体，混进来时生产环境照常迁移，本机按 README 跑迁移却在构建期失败。
/// </remarks>
public class MigratorRegistrationTests
{
    [Fact]
    public void The_migrator_registration_passes_build_time_validation()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // 真实库的注册路径（租户连接解析等）；构建容器不连库
                ["ConnectionStrings:Default"] = "Host=localhost;Database=migrator-registration;Username=postgres",
#if (LocalIdentity)
                ["DataProtection:KeysPath"] = Path.Combine(Path.GetTempPath(), $"migrator-keys-{Guid.NewGuid():N}"),
#endif
#if (RemoteTokenAuth)
                // 迁移目标回源身份服务，构造远端存储时即校验；部署时由 compose 以必填变量提供
                ["Leistd:ServiceClients:Identity:BaseAddress"] = "https://identity.example",
#endif
            })
            .Build();
        var environment = new DevelopmentEnvironment();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(environment);
        services.AddMigratorServices(configuration, environment);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<DatabaseMigrationRunner>());
    }

    private sealed class DevelopmentEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "migrator-registration";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
