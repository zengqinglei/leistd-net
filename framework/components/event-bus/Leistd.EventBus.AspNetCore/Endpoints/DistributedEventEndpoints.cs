using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Leistd.Data.Paging;
using Leistd.EventBus.Distributed.Dtos;
using Leistd.EventBus.Distributed.Management;
using Leistd.EventBus.Distributed.Stores;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
namespace Leistd.EventBus.AspNetCore.Endpoints;

/// <summary>分布式事件端点映射与稳定端点名。</summary>
public static class DistributedEventEndpoints
{
    /// <summary>宿主按端点名追加授权或审计元数据使用的前缀。</summary>
    public const string NamePrefix = "Leistd.EventBus.Distributed.";

    /// <summary>GET /status 的端点名。</summary>
    public const string GetStatusName = NamePrefix + "GetStatus";

    /// <summary>GET /catalog 的端点名。</summary>
    public const string GetCatalogName = NamePrefix + "GetCatalog";

    /// <summary>GET /databases 的端点名。</summary>
    public const string GetDatabasesName = NamePrefix + "GetDatabases";

    /// <summary>GET /outbox 的端点名。</summary>
    public const string GetOutboxListName = NamePrefix + "GetOutboxList";

    /// <summary>GET /outbox/{id:long} 的端点名。</summary>
    public const string GetOutboxName = NamePrefix + "GetOutbox";

    /// <summary>GET /outbox/{id:long}/payload 的端点名。</summary>
    public const string GetOutboxPayloadName = NamePrefix + "GetOutboxPayload";

    /// <summary>GET /inbox 的端点名。</summary>
    public const string GetInboxListName = NamePrefix + "GetInboxList";

    /// <summary>GET /inbox/{id:long} 的端点名。</summary>
    public const string GetInboxName = NamePrefix + "GetInbox";

    /// <summary>GET /inbox/{id:long}/payload 的端点名。</summary>
    public const string GetInboxPayloadName = NamePrefix + "GetInboxPayload";

    /// <summary>GET /rejections 的端点名。</summary>
    public const string GetRejectionsName = NamePrefix + "GetRejections";

    /// <summary>GET /rejections/{id:long} 的端点名。</summary>
    public const string GetRejectionName = NamePrefix + "GetRejection";

    /// <summary>GET /rejections/{id:long}/payload 的端点名。</summary>
    public const string GetRejectionPayloadName = NamePrefix + "GetRejectionPayload";

    /// <summary>POST /outbox/redeliver 的端点名。</summary>
    public const string RedeliverName = NamePrefix + "Redeliver";

    /// <summary>POST /inbox/replay 的端点名。</summary>
    public const string ReplayName = NamePrefix + "Replay";

    /// <summary>POST /rejections/redecode 的端点名。</summary>
    public const string RedecodeName = NamePrefix + "Redecode";

    /// <summary>POST /rejections/discard 的端点名。</summary>
    public const string DiscardName = NamePrefix + "Discard";

    /// <summary>POST /test 的端点名。</summary>
    public const string SendTestEventName = NamePrefix + "SendTestEvent";

    /// <summary>将管理端点映射到宿主给定的路由组，读载荷与所有写操作使用 Manage 策略。</summary>
    /// <remarks>策略缺失、管理用例或管理存储未注册时映射即失败；不叠加宿主默认策略。租户与宿主库边界由管理用例校验。</remarks>
    /// <example>
    /// <code>
    /// app.MapGroup("/api/v1/distributed-events").MapDistributedEvents(options =&gt;
    /// {
    ///     options.ReadPolicy = "App.DistributedEvents";
    ///     options.ManagePolicy = "App.DistributedEvents.Manage";
    /// });
    /// </code>
    /// </example>
    public static RouteGroupBuilder MapDistributedEvents(
        this IEndpointRouteBuilder endpoints, Action<DistributedEventEndpointOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(configure);
        var options = new DistributedEventEndpointOptions();
        configure(options);
        options.Validate();
        var probe = endpoints.ServiceProvider.GetService<IServiceProviderIsService>();
        if (probe is not null && (!probe.IsService(typeof(IDistributedEventManagementService))
            || !probe.IsService(typeof(IDistributedEventManagementStore))))
        {
            throw new InvalidOperationException("MapDistributedEvents requires AddDistributedEventBus and a management store (e.g. AddDistributedEventBusEfCore<TDbContext>()).");
        }

        var group = endpoints.MapGroup(string.Empty);
        group.MapGet("status", (IDistributedEventManagementService service, CancellationToken cancellationToken, string? database = null)
                => service.GetStatusAsync(database, cancellationToken))
            .WithName(GetStatusName)
            .RequireAuthorization(options.ReadPolicy);

        group.MapGet("catalog", (IDistributedEventManagementService service, CancellationToken cancellationToken, string? database = null)
                => service.GetCatalogAsync(database, cancellationToken))
            .WithName(GetCatalogName)
            .RequireAuthorization(options.ReadPolicy);

        group.MapGet("databases", (IDistributedEventManagementService service, CancellationToken cancellationToken)
                => service.GetDatabasesAsync(cancellationToken))
            .WithName(GetDatabasesName)
            .RequireAuthorization(options.ReadPolicy);

        group.MapGet("outbox", (IDistributedEventManagementService service, CancellationToken cancellationToken,
                int offset = 0, int limit = PageRequest.DefaultLimit,
                string? database = null, string? status = null,
                string? eventName = null, string? keyword = null, DateTime? before = null, string? errorCode = null)
                => service.GetOutboxListAsync(new GetDistributedEventOutboxInputDto
                    {
                        Offset = offset, Limit = limit, Database = database,
                        Status = ParseStatus<DistributedEventOutboxStatus>(status),
                        EventName = eventName, Keyword = keyword, Before = before,
                        ErrorCode = errorCode
                    }, cancellationToken))
            .WithName(GetOutboxListName)
            .RequireAuthorization(options.ReadPolicy);

        group.MapGet("outbox/{id:long}", (long id, IDistributedEventManagementService service, CancellationToken cancellationToken, string? database = null)
                => service.GetOutboxAsync(id, database, cancellationToken))
            .WithName(GetOutboxName)
            .RequireAuthorization(options.ReadPolicy);

        group.MapGet("outbox/{id:long}/payload", (long id, IDistributedEventManagementService service, CancellationToken cancellationToken, string? database = null)
                => service.GetOutboxPayloadAsync(id, database, cancellationToken))
            .WithName(GetOutboxPayloadName)
            .RequireAuthorization(options.ManagePolicy);

        group.MapGet("inbox", (IDistributedEventManagementService service, CancellationToken cancellationToken,
                int offset = 0, int limit = PageRequest.DefaultLimit,
                string? database = null, string? status = null,
                string? eventName = null, string? keyword = null, DateTime? before = null, string? errorCode = null, string? consumer = null)
                => service.GetInboxListAsync(new GetDistributedEventInboxInputDto
                    {
                        Offset = offset, Limit = limit, Database = database,
                        Status = ParseStatus<DistributedEventInboxStatus>(status),
                        EventName = eventName, Keyword = keyword, Before = before,
                        ErrorCode = errorCode, Consumer = consumer
                    }, cancellationToken))
            .WithName(GetInboxListName)
            .RequireAuthorization(options.ReadPolicy);

        group.MapGet("inbox/{id:long}", (long id, IDistributedEventManagementService service, CancellationToken cancellationToken, string? database = null)
                => service.GetInboxAsync(id, database, cancellationToken))
            .WithName(GetInboxName)
            .RequireAuthorization(options.ReadPolicy);

        group.MapGet("inbox/{id:long}/payload", (long id, IDistributedEventManagementService service, CancellationToken cancellationToken, string? database = null)
                => service.GetInboxPayloadAsync(id, database, cancellationToken))
            .WithName(GetInboxPayloadName)
            .RequireAuthorization(options.ManagePolicy);

        group.MapGet("rejections", (IDistributedEventManagementService service, CancellationToken cancellationToken,
                int offset = 0, int limit = PageRequest.DefaultLimit,
                string? database = null, string? status = null,
                string? eventName = null, string? keyword = null, DateTime? before = null, string? reason = null)
                => service.GetRejectionsAsync(new GetDistributedEventRejectionsInputDto
                    {
                        Offset = offset, Limit = limit, Database = database,
                        Status = ParseStatus<DistributedEventRejectionStatus>(status),
                        EventName = eventName, Keyword = keyword, Before = before,
                        Reason = reason
                    }, cancellationToken))
            .WithName(GetRejectionsName)
            .RequireAuthorization(options.ReadPolicy);

        group.MapGet("rejections/{id:long}", (long id, IDistributedEventManagementService service, CancellationToken cancellationToken, string? database = null)
                => service.GetRejectionAsync(id, database, cancellationToken))
            .WithName(GetRejectionName)
            .RequireAuthorization(options.ReadPolicy);

        group.MapGet("rejections/{id:long}/payload", (long id, IDistributedEventManagementService service, CancellationToken cancellationToken, string? database = null)
                => service.GetRejectionPayloadAsync(id, database, cancellationToken))
            .WithName(GetRejectionPayloadName)
            .RequireAuthorization(options.ManagePolicy);

        group.MapPost("outbox/redeliver", (RecoverDistributedEventOutboxInputDto input, IDistributedEventManagementService service, CancellationToken cancellationToken, string? database = null)
                => service.RedeliverAsync(input, database, cancellationToken))
            .WithName(RedeliverName)
            .RequireAuthorization(options.ManagePolicy);

        group.MapPost("inbox/replay", (RecoverDistributedEventInboxInputDto input, IDistributedEventManagementService service, CancellationToken cancellationToken, string? database = null)
                => service.ReplayAsync(input, database, cancellationToken))
            .WithName(ReplayName)
            .RequireAuthorization(options.ManagePolicy);

        group.MapPost("rejections/redecode", (RecoverDistributedEventRejectionInputDto input, IDistributedEventManagementService service, CancellationToken cancellationToken, string? database = null)
                => service.RedecodeAsync(input, database, cancellationToken))
            .WithName(RedecodeName)
            .RequireAuthorization(options.ManagePolicy);

        group.MapPost("rejections/discard", (RecoverDistributedEventRejectionInputDto input, IDistributedEventManagementService service, CancellationToken cancellationToken, string? database = null)
                => service.DiscardAsync(input, database, cancellationToken))
            .WithName(DiscardName)
            .RequireAuthorization(options.ManagePolicy);

        group.MapPost("test", (IDistributedEventManagementService service, CancellationToken cancellationToken, string? database = null)
                => service.SendTestEventAsync(database, cancellationToken))
            .WithName(SendTestEventName)
            .RequireAuthorization(options.ManagePolicy);
        return group;
    }

    private static T? ParseStatus<T>(string? status) where T : struct, Enum
    {
        if (status is null)
        {
            return null;
        }

        foreach (var value in Enum.GetValues<T>())
        {
            if (JsonNamingPolicy.CamelCase.ConvertName(value.ToString()) == status)
            {
                return value;
            }
        }

        throw new ValidationException(
            new ValidationResult("Status is not an allowed value.", ["Status"]), null, status);
    }
}
