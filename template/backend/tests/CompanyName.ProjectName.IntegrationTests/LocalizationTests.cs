using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompanyName.ProjectName.Api;
using Leistd.Exception.AspNetCore.Localization;
using Leistd.Exception.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace CompanyName.ProjectName.IntegrationTests;

public sealed class LocalizationTests(ProjectWebApplicationFactory factory)
    : IClassFixture<ProjectWebApplicationFactory>
{
    [Fact]
    public void Exception_response_localizer_uses_current_ui_culture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("zh-CN");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
            using var scope = factory.Services.CreateScope();
            var localizer = scope.ServiceProvider.GetRequiredService<IExceptionResponseLocalizer>();
            var strings = scope.ServiceProvider.GetRequiredService<IStringLocalizer<ApiResource>>();

            var result = localizer.Localize(
                new BadRequestException("The request was canceled.")
                    .WithLocalization("Exception.RequestCanceled"),
                StatusCodes.Status400BadRequest);

            Assert.Equal("请求已取消。", result.Message);
            Assert.Equal("请求错误", result.Title);

            var businessResult = localizer.Localize(
                new BadRequestException("Username 'admin' already exists.")
                    .WithCode("01")
                    .WithLocalization("Exception:40001", "admin"),
                StatusCodes.Status400BadRequest);
            Assert.Equal("用户名“admin”已存在。", businessResult.Message);
            Assert.Equal("用户名", strings["Username"]);
            Assert.Equal("{0}不能为空。", strings["{0} is required."]);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Theory]
    [InlineData("en-US", "Username is required.")]
    [InlineData("zh-CN", "用户名不能为空。")]
    public async Task Data_annotations_preserve_localized_field_meaning(string culture, string expectedMessage)
    {
        using var client = factory.CreateProjectClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            Username = string.Empty,
            Email = "invalid",
            Password = string.Empty
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = body.RootElement.GetProperty("errors");
        Assert.Contains(
            errors.GetProperty("Username").EnumerateArray().Select(item => item.GetString()),
            message => message == expectedMessage);
    }
}
