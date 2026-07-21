using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>
/// 验证后端错误消息随请求 <c>Accept-Language</c> 本地化（仅在启用多语言 + Identity 时生成）。
/// </summary>
public sealed class LocalizationTests(ProjectWebApplicationFactory factory) : IClassFixture<ProjectWebApplicationFactory>
{
    private static async Task<string?> PostBadLoginAndReadMessageAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/session-login",
            new { UsernameOrEmail = "no-such-user", Password = "wrong-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.TryGetProperty("message", out var m) ? m.GetString() : null;
    }

    [Fact]
    public async Task Business_error_message_is_english_by_default()
    {
        using var client = factory.CreateProjectClient();
        // 不带 Accept-Language → 默认语言英语
        var message = await PostBadLoginAndReadMessageAsync(client);
        Assert.NotNull(message);
        Assert.Contains("Login failed", message);
    }

    [Fact]
    public async Task Business_error_message_localizes_to_chinese()
    {
        using var client = factory.CreateProjectClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN");

        var message = await PostBadLoginAndReadMessageAsync(client);
        Assert.NotNull(message);
        Assert.Contains("登录失败", message);
    }

    [Fact]
    public async Task Same_endpoint_returns_different_message_per_culture()
    {
        using var en = factory.CreateProjectClient();
        en.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US");
        using var zh = factory.CreateProjectClient();
        zh.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN");

        var enMessage = await PostBadLoginAndReadMessageAsync(en);
        var zhMessage = await PostBadLoginAndReadMessageAsync(zh);

        Assert.NotNull(enMessage);
        Assert.NotNull(zhMessage);
        Assert.NotEqual(enMessage, zhMessage);
    }

    // ---- DataAnnotations 校验消息本地化（与业务异常同一 culture 通道）----

    private static async Task<string> PostInvalidRegisterAndReadErrorsAsync(HttpClient client)
    {
        // 空用户名 + 非法邮箱 → 触发 [ApiController] 自动 400 校验；经 ConfigureApiValidation
        // 产出与业务 422 一致的 errors 数组（每项 detail/field），校验消息在 detail 里。
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new { Username = "", Email = "not-an-email", Password = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        // 返回整个 errors 数组文本，便于按语言断言其中的校验文案（本地化后的 detail）
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("errors").GetRawText();
    }

    [Fact]
    public async Task Validation_message_is_english_by_default()
    {
        using var client = factory.CreateProjectClient();
        var errors = await PostInvalidRegisterAndReadErrorsAsync(client);
        // 英文校验句（DataAnnotations ErrorMessage 键即英文默认值）
        Assert.Contains("is required", errors);
    }

    [Fact]
    public async Task Validation_message_localizes_to_chinese()
    {
        using var client = factory.CreateProjectClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN");

        var errors = await PostInvalidRegisterAndReadErrorsAsync(client);
        Assert.Contains("不能为空", errors);
    }

    [Fact]
    public async Task Validation_and_business_error_share_the_same_culture()
    {
        // 关键一致性：同一个 zh-CN 客户端，参数校验消息与业务异常消息都应为中文
        using var zh = factory.CreateProjectClient();
        zh.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN");

        var validationErrors = await PostInvalidRegisterAndReadErrorsAsync(zh);
        var businessMessage = await PostBadLoginAndReadMessageAsync(zh);

        Assert.Contains("不能为空", validationErrors); // DataAnnotations 中文
        Assert.NotNull(businessMessage);
        Assert.Contains("登录失败", businessMessage);   // 业务异常中文
    }
}
