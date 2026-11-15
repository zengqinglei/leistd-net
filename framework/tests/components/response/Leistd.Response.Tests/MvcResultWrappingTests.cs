using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Leistd.Response.AspNetCore;
using Leistd.Response.AspNetCore.Attributes;
using Leistd.Response.AspNetCore.Extensions;
using Leistd.Response.Wrappers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Leistd.Response.Tests;

/// <summary>MVC 包装保留结果的响应头、格式化配置和声明类型。</summary>
public sealed class MvcResultWrappingTests(MvcResultWrappingTests.HostFixture fixture)
    : IClassFixture<MvcResultWrappingTests.HostFixture>
{
    [Theory]
    [InlineData("created-at", HttpStatusCode.Created)]
    [InlineData("created", HttpStatusCode.Created)]
    [InlineData("accepted", HttpStatusCode.Accepted)]
    public async Task Semantic_results_keep_their_location_while_wrapping_the_body(string action, HttpStatusCode status)
    {
        using var response = await fixture.Client.GetAsync($"/mvc-wrapper/{action}");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(status, response.StatusCode);
        Assert.EndsWith("/mvc-wrapper/item/42", response.Headers.Location?.ToString());
        Assert.Equal("order", body.GetProperty("data").GetProperty("name").GetString());
    }

    [Fact]
    public async Task A_typed_action_serializes_the_envelope_instead_of_the_original_declared_type()
    {
        var body = await fixture.Client.GetFromJsonAsync<JsonElement>("/mvc-wrapper/typed");
        Assert.Equal("order", body.GetProperty("data").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Result_specific_content_types_and_formatters_are_preserved()
    {
        using var response = await fixture.Client.GetAsync("/mvc-wrapper/formatted");
        Assert.Equal("application/x-wrapper", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("wrapped:order", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("already", true)]
    [InlineData("raw", false)]
    public async Task Existing_envelopes_and_opted_out_results_keep_their_shape(string action, bool envelope)
    {
        var body = await fixture.Client.GetFromJsonAsync<JsonElement>($"/mvc-wrapper/{action}");
        var payload = envelope ? body.GetProperty("data") : body;
        Assert.Equal("order", payload.GetProperty("name").GetString());
        Assert.False(payload.TryGetProperty("data", out _));
    }

    public sealed class HostFixture : IAsyncLifetime
    {
        private IHost _host = null!;
        public HttpClient Client { get; private set; } = null!;

        public async Task InitializeAsync()
        {
            _host = await new HostBuilder().ConfigureWebHost(web => web.UseTestServer()
                .ConfigureServices(services => services.AddControllers()
                    .AddApplicationPart(typeof(MvcWrappingController).Assembly).AddResponseWrapper())
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                })).StartAsync();
            Client = _host.GetTestClient();
        }

        public async Task DisposeAsync()
        {
            Client.Dispose();
            await _host.StopAsync();
            _host.Dispose();
        }
    }
}

[ApiController]
[Route("mvc-wrapper")]
public sealed class MvcWrappingController : ControllerBase
{
    public sealed record Payload(string Name);

    [HttpGet("item/{id:int}")]
    public Payload Item(int id) => new("order");

    [HttpGet("created-at")]
    public IActionResult CreatedAt() => CreatedAtAction(nameof(Item), new { id = 42 }, new Payload("order"));

    [HttpGet("created")]
    public IActionResult CreatedItem() => Created("/mvc-wrapper/item/42", new Payload("order"));

    [HttpGet("accepted")]
    public IActionResult AcceptedItem() => Accepted("/mvc-wrapper/item/42", new Payload("order"));

    [HttpGet("typed")]
    public ActionResult<Payload> Typed() => new Payload("order");

    [HttpGet("formatted")]
    public IActionResult Formatted()
    {
        var result = new OkObjectResult(new Payload("order"));
        result.ContentTypes.Add("application/x-wrapper");
        result.Formatters.Add(new EnvelopeFormatter());
        return result;
    }

    [HttpGet("already")]
    public Result<Payload> Already() => Result<Payload>.Ok(new Payload("order"));

    [HttpGet("raw")]
    [NoWrap]
    public Payload Raw() => new("order");

    private sealed class EnvelopeFormatter : IOutputFormatter
    {
        public bool CanWriteResult(OutputFormatterCanWriteContext context) =>
            context.Object is Result<object?> { Data: Payload };

        public Task WriteAsync(OutputFormatterWriteContext context)
        {
            context.HttpContext.Response.ContentType = "application/x-wrapper";
            var envelope = (Result<object?>)context.Object!;
            return context.HttpContext.Response.WriteAsync($"wrapped:{((Payload)envelope.Data!).Name}");
        }
    }
}
