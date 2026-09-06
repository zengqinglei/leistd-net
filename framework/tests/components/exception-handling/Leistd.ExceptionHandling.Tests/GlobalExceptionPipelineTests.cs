using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Leistd.ExceptionHandling.AspNetCore;
using Leistd.ExceptionHandling.AspNetCore.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Leistd.ExceptionHandling.Tests;

/// <summary>
/// 全局异常处理接入真实管道后的行为：注册面、排除模式与诊断抑制。
/// </summary>
/// <remarks>
/// <c>AddGlobalExceptionHandler</c> / <c>UseGlobalExceptionHandler</c> 是这个家族唯一的接入方式，
/// 此前两者都是零覆盖——handler 本身测过，"它有没有被挂上去"没测过。
/// </remarks>
public class GlobalExceptionPipelineTests
{
    private static async Task<TestServer> StartAsync(
        Action<IServiceCollection> configureServices,
        Action<IApplicationBuilder>? configureApp = null)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(configureServices)
                .Configure(app =>
                {
                    app.UseGlobalExceptionHandler();
                    configureApp?.Invoke(app);

                    // 终端中间件按路径分派，不引入路由：本组的被测对象是异常中间件本身，
                    // 端点路由只会给失败原因多一层来源
                    app.Run(async context =>
                    {
                        var path = context.Request.Path.Value ?? "";
                        if (path.StartsWith("/api/orders/", StringComparison.Ordinal))
                            throw new NotFoundException($"Order {path[12..]} not found.");
                        if (path is "/api/health/live" or "/internal/metrics")
                            throw new InvalidOperationException("probe blew up");

                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync("""{"ok":true}""");
                    });
                }))
            .StartAsync();

        return host.GetTestServer();
    }

    private static Action<IServiceCollection> WithOptions(Action<GlobalExceptionOptions>? configure = null) =>
        services => services.AddGlobalExceptionHandler(o => configure?.Invoke(o));

    [Fact]
    public async Task Business_exception_becomes_problem_details_with_its_own_status()
    {
        using var server = await StartAsync(WithOptions());

        var response = await server.CreateClient().GetAsync("/api/orders/1001");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.TryGetProperty("traceId", out _));
    }

    // 配置绑定重载与委托重载必须给出同一结果，否则宿主换一种配法行为就变了。
    [Fact]
    public async Task Configuration_bound_overload_behaves_like_the_delegate_overload()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Leistd:GlobalException:ExcludePatterns:0"] = "/api/health/**",
            })
            .Build();

        using var server = await StartAsync(services => services.AddGlobalExceptionHandler(configuration));

        // 命中排除模式：异常不被转成 ProblemDetails，原样冒泡成 500
        var excluded = await server.CreateClient().GetAsync("/api/health/live");
        Assert.Equal(HttpStatusCode.InternalServerError, excluded.StatusCode);

        // 未命中：照常处理
        var handled = await server.CreateClient().GetAsync("/api/orders/7");
        Assert.Equal(HttpStatusCode.NotFound, handled.StatusCode);
    }

    /// <summary>排除模式的三条分支：<c>/**</c> 前缀、<c>*</c> 单段通配、精确匹配。</summary>
    /// <remarks>
    /// 观察点是<b>状态码</b>而不是 Content-Type：被排除时处理器直接放行，
    /// 交回框架默认处理——默认处理器同样输出 problem+json，但拿不到业务异常的状态码，
    /// 于是 <c>NotFoundException</c> 从 404 退化成 500。按 Content-Type 断言分辨不出这件事。
    /// </remarks>
    [Theory]
    [InlineData("/api/orders/**", true)]
    [InlineData("/api/health/**", false)]
    [InlineData("/api/orders/*", true)]
    [InlineData("/internal/*", false)]
    [InlineData("/api/orders/1001", true)]
    [InlineData("/API/ORDERS/1001", true)]      // 路径比较不区分大小写
    [InlineData("/api/orders/100", false)]      // 精确匹配不做前缀
    public async Task Exclude_patterns_cover_prefix_wildcard_and_exact_forms(string pattern, bool excluded)
    {
        using var server = await StartAsync(WithOptions(o => o.ExcludePatterns = [pattern]));

        var response = await server.CreateClient().GetAsync("/api/orders/1001");

        Assert.Equal(
            excluded ? HttpStatusCode.InternalServerError : HttpStatusCode.NotFound,
            response.StatusCode);
    }

    [Fact]
    public async Task No_exclude_patterns_means_nothing_is_excluded()
    {
        using var server = await StartAsync(WithOptions());

        var response = await server.CreateClient().GetAsync("/api/orders/1001");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // Enabled=false 与"命中排除模式"走同一条放行路径，宿主排查中间件顺序时会用到。
    [Fact]
    public async Task Disabling_the_handler_hands_the_exception_back_to_the_framework()
    {
        using var server = await StartAsync(WithOptions(o => o.Enabled = false));

        var response = await server.CreateClient().GetAsync("/api/orders/1001");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    // 正常响应不得被异常中间件改写——接入位置靠前，很容易误伤成功路径。
    [Fact]
    public async Task Successful_requests_pass_through_untouched()
    {
        using var server = await StartAsync(WithOptions());

        var response = await server.CreateClient().GetAsync("/api/ok");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public void Registration_adds_problem_details_and_the_handler()
    {
        var services = new ServiceCollection();

        services.AddGlobalExceptionHandler(_ => { });

        Assert.Contains(services, d => d.ServiceType == typeof(IExceptionHandler));
        Assert.Contains(services, d => d.ServiceType == typeof(IProblemDetailsService));
    }
}
