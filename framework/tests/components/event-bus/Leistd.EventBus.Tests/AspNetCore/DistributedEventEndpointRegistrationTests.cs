using Leistd.EventBus.AspNetCore.Endpoints;
using Leistd.EventBus.Distributed.Management;
using Leistd.EventBus.Distributed.Stores;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leistd.EventBus.Tests.AspNetCore;

public sealed class DistributedEventEndpointRegistrationTests
{
    /// <summary>管理用例可独立登记，漏掉存储时仍须在映射阶段报出，不能等第一次请求才失败。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Mapping_without_a_store_rejects_even_when_management_is_registered(bool registerManagement)
    {
        var builder = WebApplication.CreateBuilder();
        if (registerManagement)
        {
            builder.Services.AddTransient<IDistributedEventManagementService>(_ =>
                throw new InvalidOperationException("Mapping must not instantiate the management service."));
        }

        await using var app = builder.Build();
        Assert.Throws<InvalidOperationException>(() => app.MapDistributedEvents(options =>
        {
            options.ReadPolicy = "events.read";
            options.ManagePolicy = "events.manage";
        }));
    }

    /// <summary>具备管理用例与存储后可以映射，检查登记元信息不提前创建带作用域的服务。</summary>
    [Fact]
    public async Task Mapping_with_management_and_a_store_does_not_resolve_them()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddTransient<IDistributedEventManagementService>(_ =>
            throw new InvalidOperationException("Mapping must not instantiate the management service."));
        builder.Services.AddTransient<IDistributedEventManagementStore>(_ =>
            throw new InvalidOperationException("Mapping must not instantiate the store."));

        await using var app = builder.Build();
        var group = app.MapDistributedEvents(options =>
        {
            options.ReadPolicy = "events.read";
            options.ManagePolicy = "events.manage";
        });

        Assert.NotNull(group);
    }

}
