#if (OpenIddictServer)
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompanyName.ProjectName.Application.OpenApplications;
using CompanyName.ProjectName.Application.OpenApplications.Dtos;
using CompanyName.ProjectName.Application.Systems.Dtos;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;

namespace CompanyName.ProjectName.IntegrationTests;

public sealed class SystemDirectoryTests(ProjectWebApplicationFactory factory) : IClassFixture<ProjectWebApplicationFactory>
{
    [Fact]
    public async Task Browser_registration_roundtrips_home_URL_and_visibility_in_properties_and_updates_the_directory()
    {
        using var admin = await factory.LoginAsync("admin", ProjectWebApplicationFactory.TestAdminPassword);
        var clientId = "directory-" + Guid.NewGuid().ToString("N");
        var input = new CreateOpenApplicationInputDto
        {
            ClientId = clientId, DisplayName = "Reports", ApplicationType = "web", ClientType = "confidential",
            SessionBound = true, HomeUrl = "https://reports.example.com/", ShowInSwitcher = true
        };
        var created = await admin.Client.PostAsJsonAsync("/api/v1/open-applications", input);
        created.EnsureSuccessStatusCode();
        var application = (await created.Content.ReadFromJsonAsync<OpenApplicationOutputDto>())!;
        Assert.Equal(input.HomeUrl, application.HomeUrl);
        Assert.True(application.ShowInSwitcher);
        Assert.Equal(input.HomeUrl, application.Properties[OpenApplicationSettings.HomeUri].GetString());
        using (var scope = factory.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            var stored = (await manager.FindByClientIdAsync(clientId))!;
            var descriptor = new OpenIddictApplicationDescriptor();
            await manager.PopulateAsync(descriptor, stored);
            descriptor.DisplayNames[CultureInfo.GetCultureInfo("zh-CN")] = "报表系统";
            await manager.UpdateAsync(stored, descriptor);
        }
        admin.Client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN");
        var list = (await admin.Client.GetFromJsonAsync<List<SystemOutputDto>>("/api/v1/systems"))!;
        Assert.Equal("identity", list[0].Key);
        Assert.True(list[0].IsCurrent);
        var entry = Assert.Single(list, system => system.Key == clientId);
#if (IncludeLocalization)
        Assert.Equal("报表系统", entry.DisplayName);
#else
        Assert.Equal("Reports", entry.DisplayName);
#endif
        Assert.Equal(input.HomeUrl, entry.HomeUrl);
        Assert.False(entry.IsCurrent);
        var updated = await admin.Client.PutAsJsonAsync($"/api/v1/open-applications/{application.Id}", new UpdateOpenApplicationInputDto
        { DisplayName = "Reports", ApplicationType = "web", ClientType = "confidential", SessionBound = true, HomeUrl = input.HomeUrl, ShowInSwitcher = false });
        updated.EnsureSuccessStatusCode();
        var hidden = (await admin.Client.GetFromJsonAsync<List<SystemOutputDto>>("/api/v1/systems"))!;
        Assert.DoesNotContain(hidden, system => system.Key == clientId);
    }

    [Theory]
    [InlineData("/relative")]
    [InlineData("http://reports.example.com")]
    [InlineData("http://localhost:4200")]
    [InlineData("")]
    public async Task Invalid_home_URLs_are_returned_as_field_errors(string homeUrl)
    {
        using var admin = await factory.LoginAsync("admin", ProjectWebApplicationFactory.TestAdminPassword);
        var response = await admin.Client.PostAsJsonAsync("/api/v1/open-applications", new
        { ClientId = "invalid-" + Guid.NewGuid().ToString("N"), ApplicationType = "web", ClientType = "confidential", SessionBound = true, HomeUrl = homeUrl, ShowInSwitcher = true });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains(json.RootElement.GetProperty("errors").EnumerateArray(), error => error.GetProperty("field").GetString() == "homeUrl");
    }

    [Fact]
    public async Task Anonymous_callers_cannot_read_the_directory()
    {
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/systems")).StatusCode);
    }
}
#endif
