using Leistd.Notifications.Abstractions;
using Leistd.Notifications.Dtos;
using Leistd.Notifications.Services;
using Leistd.TestBase;
using Leistd.Timing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leistd.Notifications.Tests;

public class NotificationPublisherTests
{
    [Fact]
    public async Task Publishing_to_a_user_writes_history_and_pushes()
    {
        var (publisher, store, sender) = Build();

        await publisher.PublishToUserAsync("user-1", New("标题"));

        Assert.Single(store.Saved);
        Assert.Equal("user-1", store.Saved[0].UserId);
        Assert.Single(sender.ToUser);
    }

    // 通知只面向人：每条都落历史。瞬态推送是 realtime 组件的职责，不在本契约内。
    [Fact]
    public async Task Every_notification_is_persisted_for_its_recipient()
    {
        var (publisher, store, _) = Build();

        await publisher.PublishToUserAsync("user-1", New("一"));
        await publisher.PublishToUserAsync("user-2", New("二"));

        Assert.Equal(["user-1", "user-2"], store.Saved.Select(x => x.UserId));
    }

    // 存储是必需依赖：漏装持久化时在解析发布器处失败，而不是静默变成"只推不落"。
    [Fact]
    public void Resolving_the_publisher_without_a_store_fails()
    {
        var sp = new ServiceCollection()
            .AddSingleton<IClock>(new FakeClock())
            .AddNotifications()
            .BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() => sp.GetRequiredService<INotificationPublisher>());
    }

    // 先落库再推送：推送失败只是这一次没送到，历史还在。
    [Fact]
    public async Task History_is_written_before_the_push()
    {
        var order = new List<string>();
        var publisher = new NotificationPublisher(
            new FakeClock(),
            [new CallbackSender(() => order.Add("push"))],
            new CallbackStore(() => order.Add("save")));

        await publisher.PublishToUserAsync("user-1", New("标题"));

        Assert.Equal(["save", "push"], order);
    }

    // 框架时间一律 UTC：调用方显式传的 Local/Unspecified 也要归一化，
    // 否则通知列表的排序基准与其它时间线对不上。
    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public async Task An_explicit_creation_time_is_normalized_to_utc(DateTimeKind kind)
    {
        var (publisher, store, _) = Build();
        var supplied = DateTime.SpecifyKind(new DateTime(2026, 3, 1, 8, 0, 0), kind);

        await publisher.PublishToUserAsync("user-1", New("标题") with { CreationTime = supplied });

        var saved = store.Saved[0].Notification.CreationTime;
        Assert.Equal(DateTimeKind.Utc, saved.Kind);
        Assert.Equal(kind == DateTimeKind.Local ? supplied.ToUniversalTime() : supplied, saved);
    }

    [Fact]
    public async Task Creation_time_is_filled_in_when_the_caller_leaves_it_unset()
    {
        var clock = new FakeClock();
        var (publisher, store, _) = Build(clock);

        await publisher.PublishToUserAsync("user-1", New("标题"));

        Assert.Equal(clock.Now, store.Saved[0].Notification.CreationTime);
    }

    private static NotificationOutputDto New(string title) => new() { Title = title };

    private static (NotificationPublisher Publisher, RecordingStore Store, RecordingSender Sender) Build(
        FakeClock? clock = null)
    {
        var store = new RecordingStore();
        var sender = new RecordingSender();
        return (new NotificationPublisher(clock ?? new FakeClock(), [sender], store), store, sender);
    }

    private sealed class RecordingStore : INotificationStore
    {
        public List<(NotificationOutputDto Notification, string UserId)> Saved { get; } = [];

        public Task SaveAsync(NotificationOutputDto notification, string userId, CancellationToken ct = default)
        {
            Saved.Add((notification, userId));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<NotificationOutputDto>> GetByUserAsync(string userId, int maxCount = 50, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<NotificationOutputDto>>([]);

        public Task MarkAsReadAsync(string notificationId, string userId, CancellationToken ct = default) => Task.CompletedTask;

        public Task MarkAllAsReadAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;

        public Task<int> GetUnreadCountAsync(string userId, CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class CallbackStore(Action onSave) : INotificationStore
    {
        public Task SaveAsync(NotificationOutputDto notification, string userId, CancellationToken ct = default)
        {
            onSave();
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<NotificationOutputDto>> GetByUserAsync(string userId, int maxCount = 50, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<NotificationOutputDto>>([]);

        public Task MarkAsReadAsync(string notificationId, string userId, CancellationToken ct = default) => Task.CompletedTask;

        public Task MarkAllAsReadAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;

        public Task<int> GetUnreadCountAsync(string userId, CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class CallbackSender(Action onSend) : INotificationSender
    {
        public Task SendToUserAsync(string userId, NotificationOutputDto notification, CancellationToken ct = default)
        {
            onSend();
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingSender : INotificationSender
    {
        public List<string> ToUser { get; } = [];

        public Task SendToUserAsync(string userId, NotificationOutputDto notification, CancellationToken ct = default)
        {
            ToUser.Add(userId);
            return Task.CompletedTask;
        }


    }
}
