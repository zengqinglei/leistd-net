using System.Net;

namespace CompanyName.ProjectName.IntegrationTests;

public sealed class HealthEndpointTests(ProjectWebApplicationFactory factory)
    : IClassFixture<ProjectWebApplicationFactory>
{
    [Fact]
    public async Task Health_endpoint_should_be_available()
    {
        using var client = factory.CreateProjectClient();
        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
