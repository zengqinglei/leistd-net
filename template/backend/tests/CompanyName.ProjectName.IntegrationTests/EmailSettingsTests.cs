#if (LocalIdentity)
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompanyName.ProjectName.Application.Settings.Provider;
using CompanyName.ProjectName.Infrastructure.Persistence;
using Leistd.Email.Smtp.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>
/// 发信参数由系统设置维护：口令加密落库、只写不读，设置经配置源覆盖配置文件、发信端从 <c>IOptionsMonitor</c> 取值，测试邮件如实报告失败。
/// </summary>
/// <remarks>用例在同一个夹具里顺序执行，结束时清掉覆盖值，回到配置文件里的基线。</remarks>
public sealed class EmailSettingsTests(ProjectWebApplicationFactory factory) : IClassFixture<ProjectWebApplicationFactory>
{
    [Fact]
    public async Task Smtp_password_is_stored_encrypted_and_never_returned()
    {
        using var admin = await LoginAdminAsync();
        await WriteAsync(admin.Client, SettingConstant.Email.SmtpPassword, "smtp-secret-value");
        try
        {
            var password = await ReadSettingAsync(admin.Client, SettingConstant.Email.SmtpPassword);
            Assert.True(password.GetProperty("isSecret").GetBoolean());
            Assert.True(password.GetProperty("hasSecretValue").GetBoolean());
            Assert.False(password.TryGetProperty("tenantValue", out var value) && value.ValueKind == JsonValueKind.String);

            // 库里是密文，不是明文
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MyProjectDbContext>();
            var stored = await db.Set<Leistd.Settings.EntityFrameworkCore.Entities.SettingRecord>()
                .IgnoreQueryFilters()
                .Where(r => r.Name == SettingConstant.Email.SmtpPassword)
                .Select(r => r.Value)
                .SingleAsync();
            Assert.DoesNotContain("smtp-secret-value", stored);
        }
        finally
        {
            await WriteAsync(admin.Client, SettingConstant.Email.SmtpPassword, null);
        }
    }

    [Fact]
    public async Task Sender_uses_setting_values_and_falls_back_to_configuration()
    {
        using var admin = await LoginAdminAsync();
        var monitor = factory.Services.GetRequiredService<IOptionsMonitor<SmtpOptions>>();
        var baselineHost = monitor.CurrentValue.Host;
        var baselineUsername = monitor.CurrentValue.Username;
        await WriteAsync(admin.Client, SettingConstant.Email.SmtpHost, "smtp.runtime.example.test");
        await WriteAsync(admin.Client, SettingConstant.Email.SmtpUsername, "mailer");

        // 账号与口令要成对设置：只设了账号时这一组校验不过，整组不生效、沿用上一组，发信端取值不抛
        Assert.Equal("smtp.runtime.example.test", monitor.CurrentValue.Host);
        Assert.Equal(baselineUsername, monitor.CurrentValue.Username);

        await WriteAsync(admin.Client, SettingConstant.Email.SmtpPassword, "runtime-secret");
        try
        {
            var options = monitor.CurrentValue;

            Assert.Equal("smtp.runtime.example.test", options.Host);
            Assert.Equal("mailer", options.Username);
            // 解密后交给发信端
            Assert.Equal("runtime-secret", options.Password);
            // 没设过的项是配置文件里的基线
            Assert.Equal("noreply@companyname-projectname.local", options.DefaultFromAddress);
            // 默认值是部署基线，不是覆盖后的当前值——否则「重置」回不到配置文件。
            // 覆盖正生效时重新取一次基线，验的是取值跳过了宿主设置配置源，而不是碰巧在它加载之前取的
            var configuration = (IConfigurationRoot)factory.Services.GetRequiredService<IConfiguration>();
            Assert.Equal("smtp.runtime.example.test", configuration[$"{SmtpOptions.SectionName}:{nameof(SmtpOptions.Host)}"]);
            Assert.Equal(baselineHost, (await ReadSettingAsync(admin.Client, SettingConstant.Email.SmtpHost)).GetProperty("defaultValue").GetString());
        }
        finally
        {
            await WriteAsync(admin.Client, SettingConstant.Email.SmtpHost, null);
            await WriteAsync(admin.Client, SettingConstant.Email.SmtpUsername, null);
            await WriteAsync(admin.Client, SettingConstant.Email.SmtpPassword, null);
        }

        // 清除覆盖值即回到配置文件
        Assert.Equal(baselineHost, monitor.CurrentValue.Host);
    }

    [Fact]
    public async Task Failed_test_email_returns_the_reason()
    {
        using var admin = await LoginAdminAsync();
        // 保留给 IANA 的丢弃端口，本机上不会有服务监听
        await WriteAsync(admin.Client, SettingConstant.Email.SmtpHost, "127.0.0.1");
        await WriteAsync(admin.Client, SettingConstant.Email.SmtpPort, "9");
        try
        {
            using var response = await admin.Client.PostAsJsonAsync("/api/v1/settings/email/test", new { To = "someone@example.test" });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(ExpectedErrorCode.Of("Setting:TestEmailFailed", "Error:BadRequest"), body.RootElement.GetProperty("code").GetString());
        }
        finally
        {
            await WriteAsync(admin.Client, SettingConstant.Email.SmtpHost, null);
            await WriteAsync(admin.Client, SettingConstant.Email.SmtpPort, null);
        }
    }

    [Fact]
    public async Task Sender_address_must_be_a_bare_mailbox()
    {
        using var admin = await LoginAdminAsync();

        using var rejected = await admin.Client.PutAsJsonAsync(
            "/api/v1/settings/current-tenant",
            new { Name = SettingConstant.Email.DefaultFromAddress, Value = "Acme <noreply@acme.test>" });

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
    }

    private Task<AuthenticatedSession> LoginAdminAsync() =>
        ProjectWebApplicationFactory.LoginAsync(factory, "admin", ProjectWebApplicationFactory.TestAdminPassword);

    private static async Task<JsonElement> ReadSettingAsync(HttpClient client, string name)
    {
        var listed = await client.GetFromJsonAsync<JsonElement>("/api/v1/settings");
        return listed.EnumerateArray().Single(s => s.GetProperty("name").GetString() == name);
    }

    private static async Task WriteAsync(HttpClient client, string name, string? value)
    {
        using var response = await client.PutAsJsonAsync("/api/v1/settings/current-tenant", new { Name = name, Value = value });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
#endif
