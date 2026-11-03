#if (LocalIdentity)
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompanyName.ProjectName.Application.OperationRecords.Provider;
using CompanyName.ProjectName.Application.Users.Dtos;
using CompanyName.ProjectName.Infrastructure.Persistence;
using Leistd.OperationRecords.Models;
using Leistd.OperationRecords.Recording;
using Leistd.Webhooks.EntityFrameworkCore.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyName.ProjectName.IntegrationTests.Webhooks;

/// <summary>示例事件 <c>user.created</c>：经用户创建接口发布，事件与投递随建用户的工作单元提交或回滚。</summary>
/// <remarks>
/// 失败注入在发布之后的成功审计：建用户、发布都已写进工作单元，审计抛出让整个工作单元回滚。
/// 订阅地址是解析不到的域名，投递只会失败，断言只看落库的事件与投递，不等接收方。
/// </remarks>
public sealed class UserCreatedWebhookTests : IDisposable
{
    private readonly ProjectWebApplicationFactory factory = new();
    private readonly FailingRecorderSwitch failRecording = new();
    private readonly WebApplicationFactory<Program> host;

    public UserCreatedWebhookTests()
    {
        host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            var descriptor = services.Last(d => d.ServiceType == typeof(IOperationRecorder));
            services.Remove(descriptor);
            services.Add(ServiceDescriptor.Describe(
                typeof(IOperationRecorder),
                provider => new FailingRecorder(
                    (IOperationRecorder)(descriptor.ImplementationInstance
                        ?? descriptor.ImplementationFactory?.Invoke(provider)
                        ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!)),
                    failRecording),
                descriptor.Lifetime));
        }));
    }

    public void Dispose()
    {
        host.Dispose();
        factory.Dispose();
    }

    [Fact]
    public async Task Creating_a_user_publishes_user_created_and_a_rollback_leaves_no_webhook_rows()
    {
        using var admin = await ProjectWebApplicationFactory.LoginAsync(host, "admin", ProjectWebApplicationFactory.TestAdminPassword);
        var subscription = await CreateSubscriptionAsync(admin.Client);

        // 回滚：审计在发布之后抛出，建用户与事件、投递一起撤回
        var eventsBefore = await CountUserCreatedEventsAsync();
        failRecording.Enabled = true;
        HttpResponseMessage rejected;
        try
        {
            rejected = await admin.Client.PostAsJsonAsync("/api/v1/users", NewUser());
        }
        finally
        {
            failRecording.Enabled = false;
        }

        Assert.Equal(HttpStatusCode.InternalServerError, rejected.StatusCode);
        Assert.Equal(eventsBefore, await CountUserCreatedEventsAsync());
        Assert.Equal(0, await CountDeliveriesAsync(subscription));

        // 提交：恰好一条 user.created 投递，载荷只带新用户的 id
        var created = await admin.Client.PostAsJsonAsync("/api/v1/users", NewUser());
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var userId = (await created.Content.ReadFromJsonAsync<UserManagementOutputDto>())!.Id;

        Assert.Equal(eventsBefore + 1, await CountUserCreatedEventsAsync());
        using var deliveries = JsonDocument.Parse(await admin.Client.GetStringAsync(
            $"/api/v1/webhooks/deliveries?subscriptionId={subscription}&eventType=user.created"));
        var delivery = Assert.Single(deliveries.RootElement.GetProperty("items").EnumerateArray());
        using var detail = JsonDocument.Parse(await admin.Client.GetStringAsync(
            $"/api/v1/webhooks/deliveries/{delivery.GetProperty("id").GetString()}"));
        using var payload = JsonDocument.Parse(detail.RootElement.GetProperty("payload").GetString()!);
        Assert.Equal("user.created", payload.RootElement.GetProperty("type").GetString());
        Assert.Equal(userId, payload.RootElement.GetProperty("data").GetProperty("userId").GetGuid());
    }

    private static async Task<Guid> CreateSubscriptionAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/webhooks/subscriptions", new
        {
            Name = "User sync",
            Url = "https://receiver.invalid/hooks",
            EventTypes = new[] { "user.created" }
        });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("subscription").GetProperty("id").GetGuid();
    }

    private async Task<int> CountUserCreatedEventsAsync()
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MyProjectDbContext>().Set<WebhookEvent>()
            .CountAsync(webhookEvent => webhookEvent.Type == "user.created");
    }

    private async Task<int> CountDeliveriesAsync(Guid subscriptionId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MyProjectDbContext>().Set<WebhookDelivery>()
            .CountAsync(delivery => delivery.SubscriptionId == subscriptionId);
    }

    private static CreateUserInputDto NewUser()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        return new CreateUserInputDto
        {
            Username = $"hook_{suffix}",
            Email = $"hook_{suffix}@example.test",
            Password = "IntegrationTests!User",
            IsActive = true
        };
    }

    private sealed class FailingRecorderSwitch
    {
        public volatile bool Enabled;
    }

    // 只让"建用户成功"这一条审计抛出；其余照常转发
    private sealed class FailingRecorder(IOperationRecorder inner, FailingRecorderSwitch failRecording) : IOperationRecorder
    {
        public Task RecordSucceededAsync(
            string action,
            OperationTarget target,
            string authorizationBasis,
            CancellationToken cancellationToken = default)
            => failRecording.Enabled && action == OperationRecordActions.UserCreated
                ? throw new InvalidOperationException("Injected failure after the webhook was published.")
                : inner.RecordSucceededAsync(action, target, authorizationBasis, cancellationToken);

        public Task RecordFailedAsync(
            string action,
            OperationTarget target,
            string authorizationBasis,
            OperationFailure failure = default)
            => inner.RecordFailedAsync(action, target, authorizationBasis, failure);
    }
}
#endif
