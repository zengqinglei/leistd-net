using Leistd.DependencyInjection.DynamicProxy;
using Leistd.UnitOfWork.Core;
using Leistd.UnitOfWork.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leistd.UnitOfWork.Tests;

public sealed class UnitOfWorkDynamicProxyTests
{
    [Fact]
    public async Task Async_unit_of_work_interceptor_is_adapted_to_castle_proxy()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddUnitOfWork();
        services.AddTransient<IExampleService, ExampleService>();

        await using var provider = (ServiceProvider)new DynamicProxyServiceRegistrationCallbackFactory()
            .CreateServiceProvider(services);
        var service = provider.GetRequiredService<IExampleService>();

        Assert.Equal(42, await service.ExecuteAsync());
    }

    public interface IExampleService
    {
        Task<int> ExecuteAsync();
    }

    public sealed class ExampleService : IExampleService
    {
        [UnitOfWork(IsDisabled = true)]
        public Task<int> ExecuteAsync() => Task.FromResult(42);
    }
}
