#if (LocalIdentity)
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using CompanyName.ProjectName.Application.Auth.AppServices;
using CompanyName.ProjectName.Application.Auth.Dtos;
using CompanyName.ProjectName.Domain.Shared.Email;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CompanyName.ProjectName.IntegrationTests;

public sealed partial class EmailVerificationChallengeTests(ProjectWebApplicationFactory factory)
    : IClassFixture<ProjectWebApplicationFactory>
{
    [Fact]
    public async Task 验证挑战与邮箱绑定_成功后不能重放()
    {
        using var host = CreateEmailVerificationHost();
        using var client = ProjectWebApplicationFactory.CreateProjectClient(host);
        var email = $"challenge-{Guid.NewGuid():N}@example.test";
        var challenge = await SendChallengeAsync(host, client, email);

        var wrongEmail = await RegisterAsync(
            client,
            $"wrong_{Guid.NewGuid():N}"[..32],
            $"other-{email}",
            challenge);
        Assert.Equal(HttpStatusCode.BadRequest, wrongEmail.StatusCode);

        using var legacyContract = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            Username = $"legacy_{Guid.NewGuid():N}"[..32],
            Email = email,
            Password = "VerificationTests!Pw",
            EmailVerificationCode = challenge.Code
        });
        Assert.Equal(HttpStatusCode.BadRequest, legacyContract.StatusCode);

        var success = await RegisterAsync(
            client,
            $"valid_{Guid.NewGuid():N}"[..32],
            email,
            challenge);
        Assert.Equal(HttpStatusCode.OK, success.StatusCode);

        var replay = await RegisterAsync(
            client,
            $"replay_{Guid.NewGuid():N}"[..32],
            email,
            challenge);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
    }

    [Fact]
    public async Task 错误验证码耗尽尝试次数后_正确验证码也被拒绝()
    {
        using var host = CreateEmailVerificationHost();
        using var client = ProjectWebApplicationFactory.CreateProjectClient(host);
        var email = $"attempts-{Guid.NewGuid():N}@example.test";
        var challenge = await SendChallengeAsync(host, client, email);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var rejected = await RegisterAsync(
                client,
                $"bad{attempt}_{Guid.NewGuid():N}"[..32],
                email,
                challenge with { Code = "000000" });
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }

        var exhausted = await RegisterAsync(
            client,
            $"exhausted_{Guid.NewGuid():N}"[..32],
            email,
            challenge);
        Assert.Equal(HttpStatusCode.BadRequest, exhausted.StatusCode);
    }

    [Fact]
    public async Task 邮件发送失败会释放挑战和频控预约()
    {
        using var host = CreateEmailVerificationHost();
        using var client = ProjectWebApplicationFactory.CreateProjectClient(host);
        var email = $"send-failure-{Guid.NewGuid():N}@example.test";
        host.Services.GetRequiredService<CapturingEmailSender>().FailNext();

        using var failed = await SendChallengeResponseAsync(client, email);
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);

        _ = await SendChallengeAsync(host, client, email);
    }

    [Fact]
    public async Task 租户A挑战不能在租户B使用_且B的拒绝不销毁A挑战()
    {
        using var host = CreateEmailVerificationHost();
        var tenantA = await CreateTenantAsync(host, $"otp-a-{Guid.NewGuid():N}"[..30]);
        var tenantB = await CreateTenantAsync(host, $"otp-b-{Guid.NewGuid():N}"[..30]);
        using var clientA = CreateTenantClient(host, tenantA);
        using var clientB = CreateTenantClient(host, tenantB);
        var email = $"shared-{Guid.NewGuid():N}@example.test";
        var challengeA = await SendChallengeAsync(host, clientA, email);

        var crossTenant = await RegisterAsync(
            clientB,
            $"tenantb_{Guid.NewGuid():N}"[..32],
            email,
            challengeA);
        Assert.Equal(HttpStatusCode.BadRequest, crossTenant.StatusCode);

        var ownTenant = await RegisterAsync(
            clientA,
            $"tenanta_{Guid.NewGuid():N}"[..32],
            email,
            challengeA);
        Assert.Equal(HttpStatusCode.OK, ownTenant.StatusCode);
    }

    [Fact]
    public async Task 同一邮箱的发送频控_按Host和租户相互隔离()
    {
        using var host = CreateEmailVerificationHost();
        var tenantA = await CreateTenantAsync(host, $"rate-a-{Guid.NewGuid():N}"[..30]);
        var tenantB = await CreateTenantAsync(host, $"rate-b-{Guid.NewGuid():N}"[..30]);
        using var hostClient = ProjectWebApplicationFactory.CreateProjectClient(host);
        using var clientA = CreateTenantClient(host, tenantA);
        using var clientB = CreateTenantClient(host, tenantB);
        var email = $"rate-{Guid.NewGuid():N}@example.test";

        Assert.Equal(HttpStatusCode.OK, (await SendChallengeResponseAsync(hostClient, email)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendChallengeResponseAsync(hostClient, email)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendChallengeResponseAsync(clientA, email)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendChallengeResponseAsync(clientB, email)).StatusCode);
    }

    private WebApplicationFactory<Program> CreateEmailVerificationHost()
    {
        return factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["UserRegistration:EnableEmailVerification"] = "true",
                    ["UserRegistration:EmailCodeMaxAttempts"] = "5"
                }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICaptchaAppService>();
                services.AddSingleton<ICaptchaAppService, AcceptingCaptchaAppService>();
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<CapturingEmailSender>();
                services.AddSingleton<IEmailSender>(provider =>
                    provider.GetRequiredService<CapturingEmailSender>());
            });
        });
    }

    private static async Task<ChallengeCredentials> SendChallengeAsync(
        WebApplicationFactory<Program> host,
        HttpClient client,
        string email)
    {
        using var response = await SendChallengeResponseAsync(client, email);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var challengeId = body.RootElement.GetProperty("challengeId").GetGuid();
        Assert.True(body.RootElement.GetProperty("expiresInSeconds").GetInt32() > 0);
        Assert.True(body.RootElement.GetProperty("retryAfterSeconds").GetInt32() > 0);
        var code = host.Services.GetRequiredService<CapturingEmailSender>().GetCode(email);
        return new ChallengeCredentials(challengeId, code);
    }

    private static Task<HttpResponseMessage> SendChallengeResponseAsync(HttpClient client, string email)
        => client.PostAsJsonAsync("/api/v1/auth/send-email-code", new
        {
            Email = email,
            CaptchaToken = "accepted",
            CaptchaCode = "accepted"
        });

    private static Task<HttpResponseMessage> RegisterAsync(
        HttpClient client,
        string username,
        string email,
        ChallengeCredentials challenge)
        => client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            Username = username,
            Email = email,
            Password = "VerificationTests!Pw",
            EmailVerification = new
            {
                challenge.ChallengeId,
                challenge.Code
            }
        });

    private static HttpClient CreateTenantClient(WebApplicationFactory<Program> host, Guid tenantId)
    {
        var client = ProjectWebApplicationFactory.CreateProjectClient(host);
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString());
        return client;
    }

    private static async Task<Guid> CreateTenantAsync(WebApplicationFactory<Program> host, string name)
    {
        using var hostAdmin = await ProjectWebApplicationFactory.LoginAsync(
            host,
            "admin",
            ProjectWebApplicationFactory.TestAdminPassword);
        using var response = await hostAdmin.Client.PostAsJsonAsync("/api/v1/tenants", new
        {
            Name = name,
            DisplayName = name,
            AdminEmail = $"admin@{name}.example.test",
            AdminPassword = "Tenant@123456"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private sealed record ChallengeCredentials(Guid ChallengeId, string Code);

    private sealed class AcceptingCaptchaAppService : ICaptchaAppService
    {
        public Task<CaptchaOutputDto> GenerateCaptchaAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new CaptchaOutputDto
            {
                CaptchaToken = "accepted",
                CaptchaImageBase64 = "data:image/svg+xml;base64,"
            });

        public Task<bool> ValidateCaptchaAsync(
            string token,
            string code,
            CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    private sealed partial class CapturingEmailSender : IEmailSender
    {
        private readonly ConcurrentDictionary<string, string> _codes = new(StringComparer.OrdinalIgnoreCase);
        private int _failNext;

        public Task SendAsync(
            string to,
            string subject,
            string htmlBody,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _failNext, 0) == 1)
            {
                throw new InvalidOperationException("Simulated email delivery failure.");
            }

            var match = VerificationCodeRegex().Match(htmlBody);
            Assert.True(match.Success, "The verification email did not contain a six-digit code.");
            _codes[to] = match.Groups[1].Value;
            return Task.CompletedTask;
        }

        public string GetCode(string email)
            => _codes.TryGetValue(email, out var code)
                ? code
                : throw new InvalidOperationException($"No verification email was sent to '{email}'.");

        public void FailNext() => Interlocked.Exchange(ref _failNext, 1);

        [GeneratedRegex(@">(\d{6})<", RegexOptions.CultureInvariant)]
        private static partial Regex VerificationCodeRegex();
    }
}
#endif
