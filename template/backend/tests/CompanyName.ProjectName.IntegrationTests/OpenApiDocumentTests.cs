using Microsoft.AspNetCore.Hosting;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>
/// 官方 OpenAPI 文档：Development 下收录控制器与组件端点，其他环境不暴露接口清单。
/// </summary>
public sealed class OpenApiDocumentTests
{
    [Fact]
    public async Task Development_serves_a_document_with_controllers_and_component_endpoints()
    {
        using var factory = new ProjectWebApplicationFactory();
        using var host = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));

        var document = await host.CreateClient().GetStringAsync("/openapi/v1.json");

        Assert.Contains("\"openapi\"", document);
        // 控制器经 ApiExplorer 收录
        Assert.Contains("\"/api/v1/users", document);
        // 组件以 Minimal API 映射的端点同样收录
        Assert.Contains("\"/api/v1/settings", document);
    }

    [Fact]
    public async Task Other_environments_do_not_expose_the_document()
    {
        using var factory = new ProjectWebApplicationFactory();

        using var response = await factory.CreateClient().GetAsync("/openapi/v1.json");

        Assert.DoesNotContain("\"openapi\"", await response.Content.ReadAsStringAsync());
    }
}
