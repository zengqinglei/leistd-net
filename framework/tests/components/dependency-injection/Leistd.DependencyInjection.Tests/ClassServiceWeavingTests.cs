using Castle.DynamicProxy;
using Leistd.DependencyInjection.DynamicProxy.Extensions;
using Leistd.DependencyInjection.DynamicProxy.Registration;
using Leistd.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leistd.DependencyInjection.Tests;

/// <summary>具体类的代理由容器激活，构造与释放各发生一次。</summary>
public sealed class ClassServiceWeavingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_class_proxy_injects_dependencies_and_preserves_its_scope(bool keyed)
    {
        var services = Services();
        if (keyed)
            services.AddKeyedScoped<ConstructedService>("service");
        else
            services.AddScoped<ConstructedService>();

        using var provider = Build(services);
        var state = provider.GetRequiredService<ConstructionState>();
        using (var scope = provider.CreateScope())
        {
            var service = Resolve(scope.ServiceProvider, keyed);
            Assert.Same(service, Resolve(scope.ServiceProvider, keyed));
            Assert.Same(state, service.GetState());
            Assert.Equal(1, state.Constructions);
            Assert.Equal(1, provider.GetRequiredService<CountingInterceptor>().Calls);
        }
        Assert.Equal(1, state.Disposals);

        using (var scope = provider.CreateScope())
        {
            Resolve(scope.ServiceProvider, keyed);
            Assert.Equal(2, state.Constructions);
        }
        Assert.Equal(2, state.Disposals);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void A_class_registered_as_a_factory_or_instance_is_rejected_before_resolution(bool keyed, bool instance)
    {
        var services = Services();
        var state = new ConstructionState();
        if (keyed)
        {
            if (instance)
                services.AddKeyedSingleton("service", new ConstructedService(state));
            else
                services.AddKeyedSingleton<ConstructedService>("service", (_, _) => new ConstructedService(state));
        }
        else if (instance)
            services.AddSingleton(new ConstructedService(state));
        else
            services.AddSingleton<ConstructedService>(_ => new ConstructedService(state));

        var exception = Assert.Throws<InvalidOperationException>(() => Build(services));
        Assert.Contains(nameof(ConstructedService), exception.Message);
        Assert.Contains("implementation type", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("interface", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static IServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ConstructionState>();
        services.AddSingleton<CountingInterceptor>();
        services.OnServiceRegistered(context =>
        {
            if (context.ServiceType == typeof(ConstructedService))
                context.AddInterceptor(typeof(CountingInterceptor));
        });
        return services;
    }

    private static ServiceProvider Build(IServiceCollection services)
    {
        var factory = new DynamicProxyServiceRegistrationCallbackFactory(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
        return (ServiceProvider)factory.CreateServiceProvider(factory.CreateBuilder(services));
    }

    private static ConstructedService Resolve(IServiceProvider provider, bool keyed) => keyed
        ? provider.GetRequiredKeyedService<ConstructedService>("service")
        : provider.GetRequiredService<ConstructedService>();

    public sealed class ConstructionState
    {
        public int Constructions { get; set; }
        public int Disposals { get; set; }
    }

    public class ConstructedService : IDisposable
    {
        private readonly ConstructionState _state;

        public ConstructedService(ConstructionState state)
        {
            _state = state;
            state.Constructions++;
        }

        public virtual ConstructionState GetState() => _state;
        public void Dispose() => _state.Disposals++;
    }

    public sealed class CountingInterceptor : IInterceptor
    {
        public int Calls { get; private set; }

        public void Intercept(IInvocation invocation)
        {
            Calls++;
            invocation.Proceed();
        }
    }
}
