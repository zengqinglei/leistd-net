using Leistd.UnitOfWork.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Leistd.UnitOfWork.Tests;

/// <summary>
/// <c>AddUnitOfWork</c> 绑定 <c>Leistd:UnitOfWork</c>，委托在其后应用。
/// </summary>
public class UnitOfWorkOptionsBindingTests
{
    // 曾经的委托重载不绑定配置节：只传委托的宿主，appsettings 里的超时静默不生效
    [Fact]
    public void The_section_is_bound_and_the_delegate_overrides_it()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{UnitOfWorkOptions.SectionName}:Timeout"] = "00:00:30",
                [$"{UnitOfWorkOptions.SectionName}:IsTransactional"] = "false",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddUnitOfWork(options => options.IsTransactional = true);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<UnitOfWorkOptions>>().Value;

        Assert.Equal(TimeSpan.FromSeconds(30), options.Timeout);
        Assert.True(options.IsTransactional);
    }
}
