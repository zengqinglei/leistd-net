using System.Security.Claims;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Leistd.RealTime;
using Leistd.RealTime.AspNetCore.SignalR;
using Xunit;

namespace Leistd.RealTime.Tests;

public class RealTimeSignalRTests
{
    [Fact]
    public async Task AddRealTime_registers_allow_all_authorizer_for_public_subscriptions()
    {
        var sp = new ServiceCollection()
            .AddRealTime()
            .BuildServiceProvider();

        var authorizer = sp.GetRequiredService<IRealtimeSubscriptionAuthorizer>();
        var allowed = await authorizer.AuthorizeAsync(new RealtimeSubscriptionContext("public-feed", UserId: null));

        Assert.IsType<AllowAllRealtimeSubscriptionAuthorizer>(authorizer);
        Assert.True(allowed);
    }

    [Fact]
    public async Task Subscribe_when_authorization_not_required_does_not_call_authorizer()
    {
        var groups = new RecordingGroupManager();
        var hub = new RealTimeHub(
            new TestCurrentUser(null),
            new ThrowingAuthorizer(),
            Options.Create(new RealTimeOptions { RequireSubscriptionAuthorization = false }),
            NullLogger<RealTimeHub>.Instance)
        {
            Context = new TestHubCallerContext("conn-public", userIdentifier: null),
            Groups = groups
        };

        await hub.Subscribe("catalog:public");

        Assert.Contains(groups.Added, x => x.ConnectionId == "conn-public" && x.GroupName == "resource:catalog:public");
    }

    [Fact]
    public async Task Subscribe_when_authorization_required_denies_forbidden_resource()
    {
        var hub = new RealTimeHub(
            new TestCurrentUser(null),
            new DenyingAuthorizer(),
            Options.Create(new RealTimeOptions { RequireSubscriptionAuthorization = true }),
            NullLogger<RealTimeHub>.Instance)
        {
            Context = new TestHubCallerContext("conn-private", "user-1"),
            Groups = new RecordingGroupManager()
        };

        await Assert.ThrowsAsync<HubException>(() => hub.Subscribe("private-resource"));
    }

    [Fact]
    public void AddRealTimeSignalR_registers_signalr_services_and_web_defaults()
    {
        var sp = new ServiceCollection()
            .AddLogging()
            .AddRealTimeSignalR(options =>
            {
                options.RealTimeHubPath = "/rt";
                options.EnableDetailedErrors = true;
            })
            .BuildServiceProvider();

        var options = sp.GetRequiredService<IOptions<RealTimeOptions>>().Value;

        Assert.Equal("/rt", options.RealTimeHubPath);
        Assert.Equal(["sub", ClaimTypes.NameIdentifier], options.UserIdClaimTypes);
        Assert.IsType<ClaimsSignalRUserIdProvider>(sp.GetRequiredService<IUserIdProvider>());
        Assert.IsType<SignalRPresenceService>(sp.GetRequiredService<IPresenceService>());
        Assert.NotNull(sp.GetRequiredService<IBusinessEventPublisher>());
    }

    [Fact]
    public void ClaimsSignalRUserIdProvider_uses_configured_claim_order_and_skips_empty_values()
    {
        var provider = new ClaimsSignalRUserIdProvider(Options.Create(new RealTimeOptions
        {
            UserIdClaimTypes = ["tenant_user", "sub"]
        }));

        var connection = CreateHubConnectionContext(new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("tenant_user", ""),
            new Claim("sub", "subject-123")
        ], "Test")));

        Assert.Equal("subject-123", provider.GetUserId(connection));
    }

    [Fact]
    public async Task Presence_tracks_user_connections_without_starting_server()
    {
        var userId = Guid.NewGuid();
        var presence = new SignalRPresenceService();
        var groups = new RecordingGroupManager();
        var hub = new RealTimeHub(
            new TestCurrentUser(userId),
            new AllowAllRealtimeSubscriptionAuthorizer(),
            Options.Create(new RealTimeOptions()),
            NullLogger<RealTimeHub>.Instance)
        {
            Context = new TestHubCallerContext("conn-presence", userIdentifier: null),
            Groups = groups
        };

        await hub.OnConnectedAsync();
        Assert.True(await presence.IsOnlineAsync(userId.ToString()));
        Assert.Contains(groups.Added, x => x.ConnectionId == "conn-presence" && x.GroupName == $"user:{userId}");

        await hub.OnDisconnectedAsync(null);
        Assert.False(await presence.IsOnlineAsync(userId.ToString()));
    }

    [Fact]
    public async Task BusinessEventPublisher_sends_event_to_resource_group_without_server()
    {
        var clientProxy = new RecordingClientProxy();
        var clients = new RecordingHubClients(clientProxy);
        var publisher = BuildBusinessEventPublisher(new TestHubContext(clients));
        var payload = new TestEvent("updated");

        await publisher.PublishToResourceAsync("product:42", "ProductUpdated", payload);

        Assert.Equal("resource:product:42", clients.LastGroupName);
        var sent = Assert.Single(clientProxy.Sent);
        Assert.Equal("ProductUpdated", sent.Method);
        Assert.Same(payload, Assert.Single(sent.Args));
    }

    private static HubConnectionContext CreateHubConnectionContext(ClaimsPrincipal user)
    {
        var connection = new DefaultConnectionContext
        {
            ConnectionId = "claim-connection",
            User = user
        };

        return new HubConnectionContext(
            connection,
            new HubConnectionContextOptions(),
            NullLoggerFactory.Instance);
    }

    private static IBusinessEventPublisher BuildBusinessEventPublisher(IHubContext<RealTimeHub> hubContext)
    {
        var services = new ServiceCollection()
            .AddSingleton(hubContext)
            .AddLogging()
            .AddRealTimeSignalR()
            .BuildServiceProvider();

        return services.GetRequiredService<IBusinessEventPublisher>();
    }

    private sealed record TestEvent(string Value);
}
