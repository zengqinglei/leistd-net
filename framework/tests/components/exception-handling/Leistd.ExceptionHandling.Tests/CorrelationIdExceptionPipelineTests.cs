using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Leistd.ExceptionHandling.AspNetCore;
using Leistd.Tracing.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Leistd.ExceptionHandling.Tests;

public sealed class CorrelationIdExceptionPipelineTests
{
    [Fact]
    public async Task Selected_inbound_id_stays_in_the_error_response_after_a_downstream_activity_starts()
    {
        string? requestIdBeforeException = null;
        string? downstreamActivityTraceId = null;
        using var host = await new HostBuilder()
            .ConfigureWebHost(web => web.UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddCorrelationId(_ => { });
                    services.AddGlobalExceptionHandler(_ => { });
                })
                .Configure(app =>
                {
                    // 让关联 ID 中间件在没有请求 Activity 时采信自定义入站值。
                    app.Use(async (_, next) =>
                    {
                        var parent = Activity.Current;
                        Activity.Current = null;
                        try
                        {
                            await next();
                        }
                        finally
                        {
                            Activity.Current = parent;
                        }
                    });
                    app.UseCorrelationId();
                    app.Use(async (_, next) =>
                    {
                        using var activity = new Activity("downstream")
                            .SetIdFormat(ActivityIdFormat.W3C)
                            .Start();
                        downstreamActivityTraceId = activity.TraceId.ToHexString();
                        await next();
                    });
                    app.UseGlobalExceptionHandler();
                    app.Run(context =>
                    {
                        requestIdBeforeException = context.TraceIdentifier;
                        throw new InvalidOperationException("private diagnostic");
                    });
                }))
            .StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("X-Correlation-Id", "caller-ABC_123");
        using var response = await host.GetTestClient().SendAsync(request);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("caller-ABC_123", requestIdBeforeException);
        Assert.Equal("caller-ABC_123", response.Headers.GetValues("X-Correlation-Id").Single());
        Assert.Equal("caller-ABC_123", body.RootElement.GetProperty("traceId").GetString());
        Assert.False(string.IsNullOrWhiteSpace(downstreamActivityTraceId));
        Assert.NotEqual("caller-ABC_123", downstreamActivityTraceId);
    }
}
