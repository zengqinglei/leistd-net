using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Leistd.RealTime.AspNetCore.SignalR;
using Leistd.Security.Users;
using Leistd.RealTime.AspNetCore.SignalR.Hubs;
using Leistd.RealTime.Abstractions;

namespace Leistd.RealTime.Tests;

internal sealed class TestCurrentUser(Guid? id) : ICurrentUser
{
    public Guid? Id { get; } = id;
    public Guid? TenantId => null;
    public bool IsAuthenticated => Id.HasValue;
    public string? Username => null;
    public string? Name => null;
    public string? Email => null;
    public string? PhoneNumber => null;
    public string[] GetRoles() => [];
    public bool IsInRole(string roleName) => false;
    public Claim? FindClaim(string claimType) => null;
    public Claim[] FindClaims(string claimType) => [];
    public Claim[] GetAllClaims() => [];
}

internal sealed class TestHubCallerContext(
    string connectionId,
    string? userIdentifier,
    CancellationToken connectionAborted = default) : HubCallerContext
{
    public override string ConnectionId { get; } = connectionId;
    public override string? UserIdentifier { get; } = userIdentifier;
    public override ClaimsPrincipal? User => null;
    public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
    public override IFeatureCollection Features { get; } = new FeatureCollection();
    public override CancellationToken ConnectionAborted { get; } = connectionAborted;
    public override void Abort() { }
}

internal sealed class RecordingGroupManager : IGroupManager
{
    public List<(string ConnectionId, string GroupName)> Added { get; } = [];
    public List<(string ConnectionId, string GroupName)> Removed { get; } = [];

    public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
    {
        Added.Add((connectionId, groupName));
        return Task.CompletedTask;
    }

    public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
    {
        Removed.Add((connectionId, groupName));
        return Task.CompletedTask;
    }
}

internal sealed class ThrowingAuthorizer : IRealTimeSubscriptionAuthorizer
{
    public Task<bool> AuthorizeAsync(RealTimeSubscriptionContext context, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Authorization should not be evaluated for public subscriptions.");
}

internal sealed class DenyingAuthorizer : IRealTimeSubscriptionAuthorizer
{
    public Task<bool> AuthorizeAsync(RealTimeSubscriptionContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}

internal sealed class TestHubContext(IHubClients clients) : IHubContext<RealTimeHub>
{
    public IHubClients Clients { get; } = clients;
    public IGroupManager Groups { get; } = new RecordingGroupManager();
}

internal sealed class RecordingHubClients(IClientProxy groupProxy) : IHubClients
{
    public string? LastGroupName { get; private set; }
    public IClientProxy All => groupProxy;

    public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => groupProxy;
    public ISingleClientProxy Client(string connectionId) => new RecordingSingleClientProxy();
    IClientProxy IHubClients<IClientProxy>.Client(string connectionId) => groupProxy;
    public IClientProxy Clients(IReadOnlyList<string> connectionIds) => groupProxy;
    public IClientProxy Group(string groupName)
    {
        LastGroupName = groupName;
        return groupProxy;
    }

    public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => groupProxy;
    public IClientProxy Groups(IReadOnlyList<string> groupNames) => groupProxy;
    public IClientProxy User(string userId) => groupProxy;
    public IClientProxy Users(IReadOnlyList<string> userIds) => groupProxy;
}

internal sealed class RecordingSingleClientProxy : ISingleClientProxy
{
    public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<T> InvokeCoreAsync<T>(string method, object?[] args, CancellationToken cancellationToken) => Task.FromResult(default(T)!);
}

internal sealed class RecordingClientProxy : IClientProxy
{
    public List<(string Method, object?[] Args)> Sent { get; } = [];

    public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
    {
        Sent.Add((method, args));
        return Task.CompletedTask;
    }
}
