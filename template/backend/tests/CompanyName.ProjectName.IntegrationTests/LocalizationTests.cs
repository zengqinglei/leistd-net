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


    /// <summary>
    /// 业务校验的<b>具体原因</b>要传到客户端，不能被通用文案盖掉。
    /// </summary>
    /// <remarks>
    /// 异常处理器按错误码查词条，查不到就按状态码归一（<c>Error:BadRequest</c> → "请求无效。"）。
    /// 抛出点只带消息不带码时，界面上就只剩那句通用话，原因只留在服务端日志里——管理员看着
    /// "请求无效"完全无从修正。这里用"给日志级别写一个非法取值"这条真实场景钉住：400、
    /// 且消息里说的是这个取值本身的问题。
    /// <para>
    /// 400 一律要带码，有静态闸门守着（<c>scripts/check-error-codes.py</c>）；这条用例守的是
    /// 另一半——码到词条这条链真的接上了。
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Business_validation_reason_reaches_the_client()
    {
        using var session = await factory.LoginAsync(
            "admin", ProjectWebApplicationFactory.TestAdminPassword);
        session.Client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN");

        var response = await session.Client.PutAsJsonAsync(
            "/api/v1/settings/current-tenant",
            new { Name = "Logging.MinimumLevel", Value = "Chatty" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var message = body.RootElement.GetProperty("message").GetString();

        Assert.NotNull(message);
        Assert.DoesNotContain("请求无效", message);
        // 非法取值与可选值都要出现在文案里，否则改不动
        Assert.Contains("Chatty", message);
        Assert.Contains("日志级别", message);
    }

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
