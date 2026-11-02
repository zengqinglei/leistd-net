#if (LocalIdentity)
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompanyName.ProjectName.Application.OperationRecords.Provider;
using CompanyName.ProjectName.Application.Permissions.Provider;
using Leistd.Authorization.Constants;

namespace CompanyName.ProjectName.IntegrationTests.Webhooks;

/// <summary>组件映射的 Webhook 管理端点在本项目里的接法：读写两级权限，订阅的五种写操作留痕。</summary>
public sealed class WebhookManagementEndpointTests(ProjectWebApplicationFactory factory)
    : AuthorizationTestBase(factory), IClassFixture<ProjectWebApplicationFactory>
{
    private const string Subscriptions = "/api/v1/webhooks/subscriptions";

    /// <summary>新建、轮换、停用、启用、删除各留一条成功记录，目标是该订阅；删除返回 204。</summary>
    [Fact]
    public async Task Subscription_writes_leave_one_success_record_each()
    {
        using var admin = await Factory.LoginAsync("admin", ProjectWebApplicationFactory.TestAdminPassword);

        var created = await admin.Client.PostAsJsonAsync(Subscriptions, new
        {
            Name = "Audited hook",
            Url = "https://receiver.invalid/hooks",
            EventTypes = new[] { "*" }
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = body.RootElement.GetProperty("subscription").GetProperty("id").GetString()!;
        Assert.StartsWith("whsec_", body.RootElement.GetProperty("secret").GetString());

        Assert.Equal(HttpStatusCode.OK, (await admin.Client.PostAsync($"{Subscriptions}/{id}/rotate-secret", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.Client.PostAsync($"{Subscriptions}/{id}/disable", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.Client.PostAsync($"{Subscriptions}/{id}/enable?retryFailed=true", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.Client.DeleteAsync($"{Subscriptions}/{id}")).StatusCode);

        foreach (var action in new[]
                 {
                     OperationRecordActions.WebhookSubscriptionCreated,
                     OperationRecordActions.WebhookSubscriptionSecretRotated,
                     OperationRecordActions.WebhookSubscriptionDisabled,
                     OperationRecordActions.WebhookSubscriptionEnabled,
                     OperationRecordActions.WebhookSubscriptionDeleted
                 })
        {
            Assert.Equal(1, await OperationRecordQueries.CountSucceededAsync(
                Factory, admin.Client, action, id, authorizationBasis: PermissionConstant.Webhooks.Manage));
        }

        // 业务拒绝（订阅已删除）回滚写入、不记成功，由失败路径按动作码补记
        Assert.Equal(HttpStatusCode.NotFound, (await admin.Client.PostAsync($"{Subscriptions}/{id}/disable", null)).StatusCode);
        Assert.Equal(1, await OperationRecordQueries.CountSucceededAsync(
            Factory, admin.Client, OperationRecordActions.WebhookSubscriptionDisabled, id));
        Assert.NotEmpty(await OperationRecordQueries.GetFailuresAsync(
            Factory, admin.Client, OperationRecordActions.WebhookSubscriptionDisabled, id));
    }

    /// <summary>只有查看权限：订阅、投递与统计可读，写端点一律 403 并按管理权限记被拒。</summary>
    [Fact]
    public async Task Read_only_members_can_read_but_every_write_is_forbidden()
    {
        using var admin = await Factory.LoginAsync("admin", ProjectWebApplicationFactory.TestAdminPassword);
        var created = await admin.Client.PostAsJsonAsync(Subscriptions, new
        {
            Name = "Read-only target",
            Url = "https://receiver.invalid/hooks",
            EventTypes = new[] { "*" }
        });
        using var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = body.RootElement.GetProperty("subscription").GetProperty("id").GetString()!;

        var member = await CreateUserAsync(admin.Client);
        await GrantAsync(PermissionGrantProviderNames.User, member.Id, PermissionConstant.Webhooks.Default);
        using var reader = await Factory.LoginAsync(member.Username, TestPassword);
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

        foreach (var path in new[]
                 {
                     "/api/v1/webhooks/event-types",
                     $"{Subscriptions}?offset=0&limit=10",
                     $"{Subscriptions}/{id}",
                     "/api/v1/webhooks/deliveries?offset=0&limit=10",
                     $"/api/v1/webhooks/statistics?startDate={today}&endDate={today}&timeZone=UTC"
                 })
        {
            using var response = await reader.Client.GetAsync(path);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"GET {path} returned {(int)response.StatusCode}");
        }

        foreach (var (method, path) in new[]
                 {
                     (HttpMethod.Post, Subscriptions),
                     (HttpMethod.Put, $"{Subscriptions}/{id}"),
                     (HttpMethod.Delete, $"{Subscriptions}/{id}"),
                     (HttpMethod.Post, $"{Subscriptions}/{id}/enable"),
                     (HttpMethod.Post, $"{Subscriptions}/{id}/disable"),
                     (HttpMethod.Post, $"{Subscriptions}/{id}/rotate-secret"),
                     (HttpMethod.Post, $"{Subscriptions}/{id}/test"),
                     (HttpMethod.Post, $"/api/v1/webhooks/deliveries/{Guid.NewGuid()}/retry")
                 })
        {
            using var request = new HttpRequestMessage(method, path) { Content = method == HttpMethod.Delete
                ? null
                : JsonContent.Create(new { Name = "x", Url = "https://receiver.invalid/x", EventTypes = new[] { "*" } }) };
            using var response = await reader.Client.SendAsync(request);
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{method} {path} returned {(int)response.StatusCode}");
        }

        Assert.Contains(
            await OperationRecordQueries.GetFailuresAsync(Factory, admin.Client, OperationRecordActions.WebhookSubscriptionDeleted, id),
            failure => failure.AuthorizationBasis == PermissionConstant.Webhooks.Manage);
        // 只读调用者看得到订阅，但写端点没有动它
        Assert.Equal(HttpStatusCode.OK, (await admin.Client.GetAsync($"{Subscriptions}/{id}")).StatusCode);
    }
}
#endif
