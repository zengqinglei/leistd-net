using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Leistd.Response.AspNetCore.Attributes;
using Leistd.Response.AspNetCore.Extensions;
using Leistd.Response.Wrappers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Leistd.Response.Tests;

/// <summary>
/// Minimal API 端点的统一响应包装：与 MVC 侧同一口径，只包 2xx 且带值的响应。
/// </summary>
/// <remarks>
/// 组件经 <c>Map*</c> 提供的端点不走 MVC 过滤器；宿主若用信封形状，靠的就是这一层。
/// 包错（把错误或文件流包进信封）与漏包（信封形状在组件端点上缺席）都只在响应体里显形。
/// </remarks>
public sealed class EndpointResultWrappingTests : IAsyncLifetime
{
    private IHost _host = default!;
    private HttpClient _client = default!;

    public async Task InitializeAsync()
    {
        _host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services => services.AddRouting())
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        var group = endpoints.MapGroup("/api").WithResultWrapper();
                        group.MapGet("/plain", () => new { Name = "order" });
                        group.MapGet("/ok", () => TypedResults.Ok(new { Name = "order" }));
                        group.MapPost("/created", () => TypedResults.Created("/api/plain", new { Name = "order" }));
                        group.MapPost("/created-envelope", ()
                            => TypedResults.Created("/api/plain", Result<object>.Ok(new { Name = "order" })));
                        group.MapGet("/file", () => TypedResults.File("x"u8.ToArray(), "text/plain", "x.txt"));
                        group.MapDelete("/empty", () => TypedResults.NoContent());
                        group.MapGet("/missing", () => TypedResults.NotFound(new { Name = "order" }));
                        group.MapGet("/already", () => Result<string>.Ok("x"));
                        group.MapGet("/raw", () => new { Name = "order" }).WithMetadata(new NoWrapAttribute());
                    });
                }))
            .StartAsync();
        _client = _host.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact]
    public async Task A_plain_value_is_wrapped()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/plain");

        Assert.Equal("order", body.GetProperty("data").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Ok_with_a_value_is_wrapped()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/ok");

        Assert.Equal("order", body.GetProperty("data").GetProperty("name").GetString());
    }

    /// <summary>
    /// <c>Created</c> 原样放行：重建成 JSON 会丢掉 <c>Location</c>，而状态码看上去还是 201。
    /// </summary>
    [Fact]
    public async Task Created_passes_through_with_its_location_header()
    {
        var response = await _client.PostAsync("/api/created", content: null);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/api/plain", response.Headers.Location?.ToString());
        Assert.Equal("order", body.GetProperty("name").GetString());
    }

    /// <summary>要让创建端点也走信封，由端点自己把信封放进结果——两种语义都在。</summary>
    [Fact]
    public async Task Created_can_carry_the_envelope_itself()
    {
        var response = await _client.PostAsync("/api/created-envelope", content: null);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/api/plain", response.Headers.Location?.ToString());
        Assert.Equal("order", body.GetProperty("data").GetProperty("name").GetString());
    }

    /// <summary>文件结果带着内容类型与文件名，包进信封等于把下载变成 JSON。</summary>
    [Fact]
    public async Task A_file_result_passes_through()
    {
        var response = await _client.GetAsync("/api/file");

        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("x", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("DELETE", "/api/empty", HttpStatusCode.NoContent)]
    [InlineData("GET", "/api/missing", HttpStatusCode.NotFound)]
    public async Task Valueless_and_unsuccessful_results_pass_through(string method, string path, HttpStatusCode expected)
    {
        var response = await _client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(expected, response.StatusCode);
        Assert.DoesNotContain("\"data\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_existing_result_is_not_wrapped_twice()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/already");

        Assert.Equal("x", body.GetProperty("data").GetString());
    }

    [Fact]
    public async Task NoWrap_metadata_opts_an_endpoint_out()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/raw");

        Assert.Equal("order", body.GetProperty("name").GetString());
    }
}
