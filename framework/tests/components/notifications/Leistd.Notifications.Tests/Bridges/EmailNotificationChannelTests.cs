using Leistd.BackgroundJobs.Queues;
using Leistd.Email.Abstractions;
using Leistd.Notifications.Channels;
using Leistd.Notifications.Errors;
using Leistd.Notifications.Publishing;
using Leistd.Notifications.Stores;
using Leistd.Notifications.Dtos;
using Leistd.Notifications.Email;
using Leistd.Notifications.Email.Recipients;
using Leistd.Notifications.Email.Channels;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leistd.Notifications.Tests.Bridges;

/// <summary>
/// 邮件渠道：只发到已验证地址、正文按纯文本编码、经后台队列异步发送。
/// </summary>
public sealed class EmailNotificationChannelTests
{
    private static (ServiceProvider Provider, CapturingSender Sender) Build(string? verifiedEmail)
    {
        var sender = new CapturingSender();
        var provider = new ServiceCollection()
            .AddLogging()
            .AddSingleton<DeferredQueue>()
            .AddSingleton<IBackgroundTaskQueue>(sp => sp.GetRequiredService<DeferredQueue>())
            .AddEmailNotifications()
            .AddSingleton<INotificationRecipientResolver>(new FixedResolver(verifiedEmail))
            .AddSingleton<IEmailSender>(sender)
            .BuildServiceProvider();
        return (provider, sender);
    }

    private static async Task DrainAsync(IServiceProvider provider)
    {
        var queue = provider.GetRequiredService<DeferredQueue>();
        foreach (var item in queue.Items)
        {
            using var scope = provider.CreateScope();
            await item(scope.ServiceProvider, CancellationToken.None);
        }
    }

    /// <summary>通知内容可能夹带用户可控文本：原样当 HTML 发出等于让邮件承载注入。</summary>
    [Fact]
    public async Task A_notification_is_mailed_as_encoded_text_through_the_queue()
    {
        var (provider, sender) = Build("ada@example.com");
        using var scope = provider.CreateScope();
        var channel = scope.ServiceProvider.GetServices<INotificationChannel>().Single(c => c.Name == EmailNotificationChannel.ChannelName);

        await channel.DeliverAsync("u1", new NotificationOutputDto
        {
            Id = "n1", Title = "Alert", Content = "<script>x</script>", CreationTime = DateTime.UtcNow
        });

        Assert.Empty(sender.Sent);
        await DrainAsync(provider);

        var message = Assert.Single(sender.Sent);
        Assert.Equal(("ada@example.com", "Alert", "&lt;script&gt;x&lt;/script&gt;"), (message.To, message.Subject, message.Body));
    }

    [Fact]
    public async Task No_verified_address_means_no_mail()
    {
        var (provider, sender) = Build(verifiedEmail: null);
        using var scope = provider.CreateScope();
        var channel = scope.ServiceProvider.GetServices<INotificationChannel>().Single();

        await channel.DeliverAsync("u1", new NotificationOutputDto { Id = "n1", Title = "Alert", CreationTime = DateTime.UtcNow });
        await DrainAsync(provider);

        Assert.Empty(sender.Sent);
    }

    // 先收下工作项、由用例决定何时执行：断言"入队时尚未发送"需要把两步分开
    private sealed class DeferredQueue : IBackgroundTaskQueue
    {
        public List<Func<IServiceProvider, CancellationToken, ValueTask>> Items { get; } = [];

        public ValueTask QueueAsync(Func<IServiceProvider, CancellationToken, ValueTask> workItem, CancellationToken cancellationToken = default)
        {
            Items.Add(workItem);
            return ValueTask.CompletedTask;
        }

        public bool TryQueue(Func<IServiceProvider, CancellationToken, ValueTask> workItem)
        {
            Items.Add(workItem);
            return true;
        }
    }

    private sealed class FixedResolver(string? email) : INotificationRecipientResolver
    {
        public Task<string?> ResolveEmailAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult(email);
    }

    private sealed class CapturingSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }
}
