using CompanyName.ProjectName.Api.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace CompanyName.ProjectName.UnitTests.Api;

/// <summary>端点声明经约定进入元数据；读取方拿到与 <c>http.route</c> 同源、以 <c>/</c> 开头的模板，未声明时为空。</summary>
public sealed class SensitiveRouteMetadataTests
{
    [Theory]
    [InlineData("api/v1/mail-receipts/{provider}/{routeKey}", "/api/v1/mail-receipts/{provider}/{routeKey}")]
    [InlineData("/api/v1/mail-receipts/{provider}/{routeKey}", "/api/v1/mail-receipts/{provider}/{routeKey}")]
    [InlineData("", "/")]
    public void A_declared_endpoint_yields_its_route_template(string pattern, string expected)
    {
        var context = Context(Endpoint(pattern, sensitive: true));

        Assert.Equal(expected, SensitiveRouteMetadata.SensitiveRouteTemplate(context));
    }

    [Fact]
    public void An_undeclared_endpoint_or_no_endpoint_yields_null()
    {
        Assert.Null(SensitiveRouteMetadata.SensitiveRouteTemplate(Context(Endpoint("api/v1/items/{id}", sensitive: false))));
        Assert.Null(SensitiveRouteMetadata.SensitiveRouteTemplate(new DefaultHttpContext()));
    }

    /// <summary>异常处理重新执行前清掉了当前端点：从异常处理特性里取原端点。</summary>
    [Fact]
    public void The_original_endpoint_is_used_after_the_exception_handler_cleared_it()
    {
        var context = new DefaultHttpContext();
        context.Features.Set<IExceptionHandlerFeature>(new ExceptionHandlerFeature
        {
            Error = new InvalidOperationException(),
            Endpoint = Endpoint("hooks/{key}", sensitive: true)
        });

        Assert.Equal("/hooks/{key}", SensitiveRouteMetadata.SensitiveRouteTemplate(context));
    }

    private static Endpoint Endpoint(string pattern, bool sensitive)
    {
        var builder = new RouteEndpointBuilder(_ => Task.CompletedTask, RoutePatternFactory.Parse(pattern), 0);
        if (sensitive)
            new Conventions(builder).WithSensitiveRoute();
        return builder.Build();
    }

    private static DefaultHttpContext Context(Endpoint endpoint)
    {
        var context = new DefaultHttpContext();
        context.SetEndpoint(endpoint);
        return context;
    }

    // 约定立即作用到给定的端点构建器，等同于端点数据源构建时依次执行约定
    private sealed class Conventions(EndpointBuilder builder) : IEndpointConventionBuilder
    {
        public void Add(Action<EndpointBuilder> convention) => convention(builder);
    }
}
