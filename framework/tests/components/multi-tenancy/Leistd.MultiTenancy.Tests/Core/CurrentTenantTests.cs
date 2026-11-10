using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Leistd.MultiTenancy.Context;
using Leistd.TestBase.Doubles;

namespace Leistd.MultiTenancy.Tests.Core;

public class CurrentTenantTests
{
    private static ICurrentTenant CreateCurrentTenant() =>
        new CurrentTenant(AsyncLocalCurrentTenantAccessor.Instance);

    // 切换租户即打开日志作用域：各切换入口的日志都能按同一个键过滤，切回宿主写 null 覆盖外层租户
    [Fact]
    public void Change_puts_the_tenant_into_the_log_scope()
    {
        var logger = new FakeLogger<CurrentTenant>();
        var currentTenant = new CurrentTenant(AsyncLocalCurrentTenantAccessor.Instance, logger);
        var tenantId = Guid.NewGuid();

        using (currentTenant.Change(tenantId))
        {
            logger.LogInformation("inside tenant");
            using (currentTenant.Change(null))
            {
                logger.LogInformation("back to host");
            }
        }

        logger.LogInformation("outside");

        Assert.Equal(tenantId, ScopedTenant(logger.Collector.GetSnapshot()[0]));
        Assert.Null(ScopedTenant(logger.Collector.GetSnapshot()[1]));
        Assert.Empty(logger.Collector.GetSnapshot()[2].Scopes);
    }

    // 最内层作用域里的租户键
    private static object? ScopedTenant(FakeLogRecord record)
        => record.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>()
            .SelectMany(scope => scope)
            .Last(pair => pair.Key == TenantLogKeys.TenantId).Value;

    // 切换时交给日志的作用域：结构化提供程序（JSON、OpenTelemetry）只枚举到租户这一个键
    [Theory]
    [InlineData("11111111-1111-1111-1111-111111111111")]
    [InlineData(null)]
    public void The_tenant_scope_carries_only_the_tenant_key(string? tenant)
    {
        var tenantId = tenant is null ? (Guid?)null : Guid.Parse(tenant);

        var scope = Assert.Single(ScopesWhileChangedTo(tenantId));

        var pair = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<KeyValuePair<string, object?>>>(scope));
        Assert.Equal(TenantLogKeys.TenantId, pair.Key);
        Assert.Equal(tenantId, pair.Value);
        Assert.Equal($"leistd.tenantId:{tenant}", scope!.ToString());
    }

    // Simple 控制台格式只调用 ToString()：作用域行要是可读的 键:值，切回宿主时值为空
    [Theory]
    [InlineData("11111111-1111-1111-1111-111111111111")]
    [InlineData(null)]
    public void Simple_console_prints_the_tenant_scope_as_key_and_value(string? tenant)
    {
        var output = RenderWhileChangedTo(ConsoleFormatterNames.Simple, tenant is null ? null : Guid.Parse(tenant));

        Assert.Contains($"=> leistd.tenantId:{tenant}{Environment.NewLine}", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Json_console_writes_the_tenant_scope_as_a_property()
    {
        var tenantId = Guid.NewGuid();

        var output = RenderWhileChangedTo(ConsoleFormatterNames.Json, tenantId);

        Assert.Contains($"\"leistd.tenantId\":\"{tenantId}\"", output, StringComparison.Ordinal);
    }

    private static string RenderWhileChangedTo(string formatterName, Guid? tenantId)
    {
        using var capture = new ConsoleFormatterCapture(formatterName);
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(capture));
        var logger = loggerFactory.CreateLogger<CurrentTenant>();
        var currentTenant = new CurrentTenant(AsyncLocalCurrentTenantAccessor.Instance, logger);

        using (currentTenant.Change(tenantId))
        {
            logger.LogInformation("inside");
        }

        return capture.Output;
    }

    private static IReadOnlyList<object?> ScopesWhileChangedTo(Guid? tenantId)
    {
        var logger = new FakeLogger<CurrentTenant>();
        var currentTenant = new CurrentTenant(AsyncLocalCurrentTenantAccessor.Instance, logger);

        using (currentTenant.Change(tenantId))
        {
            logger.LogInformation("inside");
        }

        return logger.Collector.LatestRecord.Scopes;
    }

    [Fact]
    public void Change_nests_and_restores_parent_on_dispose()
    {
        var currentTenant = CreateCurrentTenant();
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        Assert.Null(currentTenant.Id);
        Assert.False(currentTenant.IsAvailable);

        using (currentTenant.Change(t1, "acme"))
        {
            Assert.Equal(t1, currentTenant.Id);
            Assert.Equal("acme", currentTenant.Name);
            Assert.True(currentTenant.IsAvailable);

            using (currentTenant.Change(t2))
            {
                Assert.Equal(t2, currentTenant.Id);
            }

            // 内层释放后恢复外层租户
            Assert.Equal(t1, currentTenant.Id);

            using (currentTenant.Change(null))
            {
                // 显式切换到宿主视角
                Assert.Null(currentTenant.Id);
                Assert.False(currentTenant.IsAvailable);
            }

            Assert.Equal(t1, currentTenant.Id);
        }

        Assert.Null(currentTenant.Id);
    }

    [Fact]
    public void Dispose_twice_restores_only_once()
    {
        var currentTenant = CreateCurrentTenant();
        var outer = Guid.NewGuid();
        var inner = Guid.NewGuid();

        using (currentTenant.Change(outer))
        {
            var handle = currentTenant.Change(inner);
            handle.Dispose();
            Assert.Equal(outer, currentTenant.Id);

            // 二次释放不应把上下文再改一次
            using (currentTenant.Change(Guid.NewGuid()))
            {
                handle.Dispose();
                Assert.NotEqual(outer, currentTenant.Id);
            }
        }
    }

    [Fact]
    public async Task Tenant_context_flows_across_await_and_tasks()
    {
        var currentTenant = CreateCurrentTenant();
        var tenantId = Guid.NewGuid();

        using (currentTenant.Change(tenantId))
        {
            await Task.Delay(1);
            Assert.Equal(tenantId, currentTenant.Id);

            // AsyncLocal 挂在 ExecutionContext 上，Task.Run 一样看得见
            var observed = await Task.Run(() => CreateCurrentTenant().Id);
            Assert.Equal(tenantId, observed);
        }

        Assert.Null(currentTenant.Id);
    }

    [Fact]
    public void Tenant_context_flows_into_new_di_scope()
    {
        // 本地事件总线为每次发布创建新 DI Scope，租户上下文必须穿透 Scope 边界
        var services = new ServiceCollection();
        services.AddMultiTenancyCore();
        using var provider = services.BuildServiceProvider();

        var currentTenant = provider.GetRequiredService<ICurrentTenant>();
        var tenantId = Guid.NewGuid();

        using (currentTenant.Change(tenantId))
        {
            using var scope = provider.CreateScope();
            var scopedTenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
            Assert.Equal(tenantId, scopedTenant.Id);
        }
    }
}
