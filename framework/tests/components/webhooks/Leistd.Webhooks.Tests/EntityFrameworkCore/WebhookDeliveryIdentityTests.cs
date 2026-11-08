using System.Collections.Concurrent;
using System.Security.Claims;
using Leistd.AmbientContext;
using Leistd.BackgroundJobs.InProcess;
using Leistd.BackgroundJobs.Queues;
using Leistd.EventBus.Abstractions;
using Leistd.EventBus.EventHandlers;
using Leistd.EventBus.Local;
using Leistd.Lock.Memory;
using Leistd.MultiTenancy.Context;
using Leistd.Security;
using Leistd.Security.Users;
using Leistd.Webhooks.Delivery;
using Leistd.Webhooks.Management;
using Leistd.Webhooks.Events;
using Leistd.Webhooks.Tests.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Leistd.Webhooks.Tests.EntityFrameworkCore;

/// <summary>
/// 立即投递在系统身份下执行：真实进程内队列入队时捕获请求用户并在执行时还原，
/// 组件须把主体换成匿名，只保留租户；与兜底扫描一致，自动停用不记到发起请求的用户名下。
/// </summary>
public sealed class WebhookDeliveryIdentityTests : IAsyncLifetime
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);
    private static readonly Guid RequestUserId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private readonly IdentityProbe _probe = new();
    private WebhookTestHost _host = null!;
    private IHostedService[] _hostedServices = [];

    public async Task InitializeAsync()
    {
        _host = await WebhookTestHost.CreateAsync(
            services =>
            {
                services.AddAmbientContext();
                services.RemoveAll<IBackgroundTaskQueue>();
                services.AddInProcessBackgroundJobs();
                services.AddMemoryLocalLock();
                services.RemoveAll<ILocalEventBus>();
                services.AddLocalEventBus();
                services.AddSingleton(_probe);
                services.RemoveAll<IWebhookSender>();
                services.AddTransient<IWebhookSender, IdentityRecordingSender>();
                services.AddScoped<IEventHandler<WebhookSubscriptionChangedEvent>, IdentityRecordingHandler>();
            },
            new Dictionary<string, string?> { ["Leistd:BackgroundJobs:Enabled"] = "false" });

        _hostedServices = [.. _host.Services.GetServices<IHostedService>()];
        foreach (var hostedService in _hostedServices)
        {
            await hostedService.StartAsync(CancellationToken.None);
        }
    }

    public async Task DisposeAsync()
    {
        foreach (var hostedService in _hostedServices.Reverse())
        {
            await hostedService.StopAsync(CancellationToken.None);
        }

        await _host.DisposeAsync();
    }

    /// <summary>用户请求里发布的事件：发送与 410 自动停用的变更事件处理器里都没有当前用户，租户仍是入队时的租户。</summary>
    [Fact]
    public async Task An_immediate_delivery_runs_without_the_requesting_user()
    {
        var (subscription, _) = await _host.AddSubscriptionAsync(WebhookTestHost.TenantA);
        _host.Sender.Enqueue(ScriptedWebhookSender.Status(410));

        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, RequestUserId.ToString())], "test"));
            using (scope.ServiceProvider.GetRequiredService<IAmbientContext>().Begin(user))
            {
                // 对照：入队时确实带着请求用户，下面的"没有用户"不是主体从未建立
                Assert.Equal(RequestUserId, scope.ServiceProvider.GetRequiredService<ICurrentUser>().Id);
                await _host.PublishAsync(WebhookTestHost.TenantA);
            }
        }

        var handled = await WaitAsync(() => Task.FromResult(_probe.Handled.IsEmpty ? null : _probe.Handled.ToArray()));
        var sent = Assert.Single(_probe.Sent);
        Assert.Equal(new ObservedIdentity(false, null, WebhookTestHost.TenantA), sent);
        Assert.All(handled, x => Assert.Equal(new ObservedIdentity(false, null, WebhookTestHost.TenantA), x));

        // 回调可能先于事务提交被观测；以持久状态完成作为断言入口，避免读取旧快照。
        var stored = await WaitAsync(async () =>
        {
            var current = await _host.SubscriptionAsync(subscription.Id);
            return current.DisabledReason == WebhookDisabledReason.EndpointGone ? current : null;
        });
        Assert.Equal(WebhookDisabledReason.EndpointGone, stored.DisabledReason);
    }

    private static async Task<T> WaitAsync<T>(Func<Task<T?>> probe) where T : class
    {
        using var timeout = new CancellationTokenSource(WaitTimeout);
        while (true)
        {
            if (await probe() is { } value)
            {
                return value;
            }

            if (timeout.IsCancellationRequested)
            {
                throw new TimeoutException($"Nothing observed within {WaitTimeout}.");
            }

            await Task.Delay(50, CancellationToken.None);
        }
    }

    private sealed record ObservedIdentity(bool IsAuthenticated, Guid? UserId, Guid? TenantId);

    private sealed class IdentityProbe
    {
        public ConcurrentQueue<ObservedIdentity> Sent { get; } = new();

        public ConcurrentQueue<ObservedIdentity> Handled { get; } = new();
    }

    private static ObservedIdentity Observe(ICurrentUser user, ICurrentTenant tenant)
        => new(user.IsAuthenticated, user.Id, tenant.Id);

    private sealed class IdentityRecordingSender(
        ScriptedWebhookSender inner,
        ICurrentUser user,
        ICurrentTenant tenant,
        IdentityProbe probe) : IWebhookSender
    {
        public Task<WebhookSendResult> SendAsync(WebhookSendRequest request, CancellationToken cancellationToken = default)
        {
            probe.Sent.Enqueue(Observe(user, tenant));
            return inner.SendAsync(request, cancellationToken);
        }
    }

    private sealed class IdentityRecordingHandler(
        ICurrentUser user,
        ICurrentTenant tenant,
        IdentityProbe probe) : IEventHandler<WebhookSubscriptionChangedEvent>
    {
        public Task HandleAsync(WebhookSubscriptionChangedEvent @event, CancellationToken cancellationToken = default)
        {
            probe.Handled.Enqueue(Observe(user, tenant));
            return Task.CompletedTask;
        }
    }
}
