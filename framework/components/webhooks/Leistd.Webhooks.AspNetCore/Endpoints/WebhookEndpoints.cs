using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Leistd.Data.Paging;
using Leistd.Webhooks.Delivery;
using Leistd.Webhooks.Dtos;
using Leistd.Webhooks.Management;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Leistd.Webhooks.AspNetCore.Endpoints;

/// <summary>webhook 后台管理的 HTTP 端点。</summary>
public static class WebhookEndpoints
{
    /// <summary>端点名前缀，宿主按名字给个别端点追加约定时使用。</summary>
    public const string NamePrefix = "Leistd.Webhooks.";

    /// <summary>
    /// 映射 webhook 管理端点：<c>GET /event-types</c>；<c>GET /subscriptions</c>、<c>GET /subscriptions/{id}</c>、
    /// <c>POST /subscriptions</c>、<c>PUT /subscriptions/{id}</c>、<c>DELETE /subscriptions/{id}</c>、
    /// <c>POST /subscriptions/{id}/enable</c>、<c>POST /subscriptions/{id}/disable</c>、<c>POST /subscriptions/{id}/rotate-secret</c>、
    /// <c>POST /subscriptions/{id}/test</c>；<c>GET /deliveries</c>、<c>GET /deliveries/{id}</c>、<c>POST /deliveries/{id}/retry</c>。
    /// </summary>
    /// <remarks>
    /// <para>读端点走 <see cref="WebhookEndpointOptions.ReadPolicy"/>，写端点走 <see cref="WebhookEndpointOptions.ManagePolicy"/>；
    /// 路由组上不叠宿主的默认策略。前缀由宿主的路由组决定；返回的路由组可继续追加约定。</para>
    /// <para>列表的查询参数：<c>offset</c>、<c>limit</c>、<c>keyword</c>；订阅另有 <c>isEnabled</c>、<c>eventType</c>，
    /// 投递另有 <c>subscriptionId</c>、<c>status</c>、<c>eventType</c>。<c>status</c> 只接受 camelCase 名称
    /// <c>pending</c>、<c>succeeded</c>、<c>failed</c>，其他值是字段 <c>status</c> 的验证错误。
    /// 启用可带 <c>retryFailed=true</c>，重发该订阅现存的全部失败投递。</para>
    /// <para>删除返回 204，其余写端点返回改后的订阅或投递；创建与轮换的结果含一次性明文密钥。</para>
    /// <para>要求已注册 <see cref="IWebhookManagementService"/>（由存储适配注册），否则映射时抛出。</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// app.MapGroup("/api/v1/webhooks").MapWebhooks(options =&gt;
    /// {
    ///     options.ReadPolicy = "App.Webhooks";
    ///     options.ManagePolicy = "App.Webhooks.Manage";
    /// });
    /// </code>
    /// </example>
    /// <param name="endpoints">路由构建器（通常是宿主的路由组）。</param>
    /// <param name="configure">授权口径。</param>
    /// <returns>承载这组端点的路由组。</returns>
    public static RouteGroupBuilder MapWebhooks(
        this IEndpointRouteBuilder endpoints,
        Action<WebhookEndpointOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new WebhookEndpointOptions();
        configure(options);
        options.Validate();

        // 管理用例由存储适配注册；映射时报出，而不是等第一次请求才 500。
        var probe = endpoints.ServiceProvider.GetService<IServiceProviderIsService>();
        if (probe is not null && !probe.IsService(typeof(IWebhookManagementService)))
        {
            throw new InvalidOperationException(
                "MapWebhooks requires a webhook store. Register one (e.g. AddWebhooksEfCore<TDbContext>()) before mapping the endpoints.");
        }

        // 不在组上套无参 RequireAuthorization：每个端点都已声明具名策略。
        var group = endpoints.MapGroup(string.Empty);

        group.MapGet("event-types", (IWebhookManagementService service, CancellationToken cancellationToken)
                => service.GetEventTypesAsync(cancellationToken))
            .WithName(NamePrefix + "GetEventTypes")
            .RequireAuthorization(options.ReadPolicy);

        MapSubscriptions(group, options);
        MapDeliveries(group, options);
        return group;
    }

    private static void MapSubscriptions(RouteGroupBuilder group, WebhookEndpointOptions options)
    {
        group.MapGet("subscriptions", (
                IWebhookManagementService service,
                CancellationToken cancellationToken,
                int offset = 0,
                int limit = PageRequest.DefaultLimit,
                string? keyword = null,
                bool? isEnabled = null,
                string? eventType = null)
                => service.GetSubscriptionsAsync(
                    new GetWebhookSubscriptionsInputDto
                    {
                        Offset = offset,
                        Limit = limit,
                        Keyword = keyword,
                        IsEnabled = isEnabled,
                        EventType = eventType
                    },
                    cancellationToken))
            .WithName(NamePrefix + "GetSubscriptions")
            .RequireAuthorization(options.ReadPolicy);

        group.MapGet("subscriptions/{id:guid}", (Guid id, IWebhookManagementService service, CancellationToken cancellationToken)
                => service.GetSubscriptionAsync(id, cancellationToken))
            .WithName(NamePrefix + "GetSubscription")
            .RequireAuthorization(options.ReadPolicy);

        group.MapPost("subscriptions", (
                CreateWebhookSubscriptionInputDto input,
                IWebhookManagementService service,
                CancellationToken cancellationToken)
                => service.CreateSubscriptionAsync(input, cancellationToken))
            .WithName(NamePrefix + "CreateSubscription")
            .RequireAuthorization(options.ManagePolicy);

        group.MapPut("subscriptions/{id:guid}", (
                Guid id,
                UpdateWebhookSubscriptionInputDto input,
                IWebhookManagementService service,
                CancellationToken cancellationToken)
                => service.UpdateSubscriptionAsync(id, input, cancellationToken))
            .WithName(NamePrefix + "UpdateSubscription")
            .RequireAuthorization(options.ManagePolicy);

        group.MapDelete("subscriptions/{id:guid}", async (Guid id, IWebhookManagementService service, CancellationToken cancellationToken) =>
            {
                await service.DeleteSubscriptionAsync(id, cancellationToken);
                return TypedResults.NoContent();
            })
            .WithName(NamePrefix + "DeleteSubscription")
            .RequireAuthorization(options.ManagePolicy);

        group.MapPost("subscriptions/{id:guid}/enable", (
                Guid id,
                IWebhookManagementService service,
                CancellationToken cancellationToken,
                bool retryFailed = false)
                => service.EnableSubscriptionAsync(id, retryFailed, cancellationToken))
            .WithName(NamePrefix + "EnableSubscription")
            .RequireAuthorization(options.ManagePolicy);

        group.MapPost("subscriptions/{id:guid}/disable", (Guid id, IWebhookManagementService service, CancellationToken cancellationToken)
                => service.DisableSubscriptionAsync(id, cancellationToken))
            .WithName(NamePrefix + "DisableSubscription")
            .RequireAuthorization(options.ManagePolicy);

        group.MapPost("subscriptions/{id:guid}/rotate-secret", (Guid id, IWebhookManagementService service, CancellationToken cancellationToken)
                => service.RotateSecretAsync(id, cancellationToken))
            .WithName(NamePrefix + "RotateSecret")
            .RequireAuthorization(options.ManagePolicy);

        group.MapPost("subscriptions/{id:guid}/test", (Guid id, IWebhookManagementService service, CancellationToken cancellationToken)
                => service.SendTestEventAsync(id, cancellationToken))
            .WithName(NamePrefix + "SendTestEvent")
            .RequireAuthorization(options.ManagePolicy);
    }

    private static void MapDeliveries(RouteGroupBuilder group, WebhookEndpointOptions options)
    {
        group.MapGet("deliveries", (
                IWebhookManagementService service,
                CancellationToken cancellationToken,
                int offset = 0,
                int limit = PageRequest.DefaultLimit,
                string? keyword = null,
                Guid? subscriptionId = null,
                string? status = null,
                string? eventType = null)
                => service.GetDeliveriesAsync(
                    new GetWebhookDeliveriesInputDto
                    {
                        Offset = offset,
                        Limit = limit,
                        Keyword = keyword,
                        SubscriptionId = subscriptionId,
                        Status = ParseStatus(status),
                        EventType = eventType
                    },
                    cancellationToken))
            .WithName(NamePrefix + "GetDeliveries")
            .RequireAuthorization(options.ReadPolicy);

        group.MapGet("deliveries/{id:guid}", (Guid id, IWebhookManagementService service, CancellationToken cancellationToken)
                => service.GetDeliveryAsync(id, cancellationToken))
            .WithName(NamePrefix + "GetDelivery")
            .RequireAuthorization(options.ReadPolicy);

        group.MapPost("deliveries/{id:guid}/retry", (Guid id, IWebhookManagementService service, CancellationToken cancellationToken)
                => service.RetryDeliveryAsync(id, cancellationToken))
            .WithName(NamePrefix + "RetryDelivery")
            .RequireAuthorization(options.ManagePolicy);
    }

    // 查询串不经宿主的 JSON 枚举转换器，按公开名称逐字匹配。不用 Enum.TryParse：它接受数字、未定义值与任意大小写。
    private static WebhookDeliveryStatus? ParseStatus(string? status)
    {
        if (status is null)
        {
            return null;
        }

        foreach (var value in Enum.GetValues<WebhookDeliveryStatus>())
        {
            if (string.Equals(JsonNamingPolicy.CamelCase.ConvertName(value.ToString()), status, StringComparison.Ordinal))
            {
                return value;
            }
        }

        throw new ValidationException(
            new ValidationResult(
                $"{nameof(GetWebhookDeliveriesInputDto.Status)} is not an allowed value.",
                [nameof(GetWebhookDeliveriesInputDto.Status)]),
            validatingAttribute: null,
            value: status);
    }
}
