#if (LocalIdentity)
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompanyName.ProjectName.Application.OperationRecords.Provider;
using CompanyName.ProjectName.Application.Permissions.Provider;
using Leistd.EventBus.Abstractions;
using Leistd.Webhooks.Events;
using Leistd.Webhooks.Management;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyName.ProjectName.IntegrationTests.Webhooks;

/// <summary>订阅变更事件转成操作记录（<c>WebhookSubscriptionChangedAuditHandler</c>）：管理员的五种写操作各留一条，自动停用不记。</summary>
public sealed class WebhookSubscriptionAuditTests(ProjectWebApplicationFactory factory)
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

        // 业务拒绝（订阅已删除）回滚写入、不发事件、不记成功，由失败路径按动作码补记
        Assert.Equal(HttpStatusCode.NotFound, (await admin.Client.PostAsync($"{Subscriptions}/{id}/disable", null)).StatusCode);
        Assert.Equal(1, await OperationRecordQueries.CountSucceededAsync(
            Factory, admin.Client, OperationRecordActions.WebhookSubscriptionDisabled, id));
        Assert.NotEmpty(await OperationRecordQueries.GetFailuresAsync(
            Factory, admin.Client, OperationRecordActions.WebhookSubscriptionDisabled, id));
    }

    /// <summary>投递链路的自动停用（端点已注销、连续失败）没有操作者，不记；同一路径上的手动停用照记（对照）。</summary>
    [Fact]
    public async Task Automatic_disables_are_not_recorded()
    {
        using var admin = await Factory.LoginAsync("admin", ProjectWebApplicationFactory.TestAdminPassword);
        var automatic = Guid.NewGuid();
        var manual = Guid.NewGuid();

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var events = scope.ServiceProvider.GetRequiredService<ILocalEventBus>();
            foreach (var reason in new[] { WebhookDisabledReason.EndpointGone, WebhookDisabledReason.ConsecutiveFailures })
            {
                await events.PublishAsync(new WebhookSubscriptionChangedEvent(
                    automatic, "Auto-disabled hook", WebhookSubscriptionChangeKind.Disabled, reason));
            }

            await events.PublishAsync(new WebhookSubscriptionChangedEvent(
                manual, "Manually disabled hook", WebhookSubscriptionChangeKind.Disabled, WebhookDisabledReason.Manual));
        }

        Assert.Equal(0, await OperationRecordQueries.CountSucceededAsync(
            Factory, admin.Client, OperationRecordActions.WebhookSubscriptionDisabled, automatic.ToString()));
        Assert.Equal(1, await OperationRecordQueries.CountSucceededAsync(
            Factory, admin.Client, OperationRecordActions.WebhookSubscriptionDisabled, manual.ToString()));
    }
}
#endif
