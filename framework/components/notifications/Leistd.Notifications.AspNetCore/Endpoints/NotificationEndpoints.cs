using System.ComponentModel.DataAnnotations;
using Leistd.Data.Paging;
using Leistd.ExceptionHandling;
using Leistd.Notifications.Errors;
using Leistd.Notifications.Stores;
using Leistd.Notifications.Dtos;
using Leistd.Security.Users;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Leistd.Notifications.AspNetCore.Endpoints;

/// <summary>当前用户的通知中心端点。</summary>
public static class NotificationEndpoints
{
    /// <summary>端点名前缀，宿主按名字给个别端点追加约定时使用。</summary>
    public const string NamePrefix = "Leistd.Notifications.";

    /// <summary>
    /// 映射通知中心：<c>GET /</c>、<c>GET /unread-count</c>、<c>PUT /{id}/read</c>、<c>PUT /read-all</c>、
    /// <c>DELETE /</c>、<c>DELETE /{id}</c>。
    /// </summary>
    /// <remarks>
    /// <para>全部端点走 <see cref="NotificationEndpointOptions.AccessPolicy"/>，且只作用于当前用户自己的通知：
    /// 收件人取自当前主体的用户标识，不接受参数指定。
    /// 当前身份不是用户（如机器客户端）时，读取返回空、写入返回带码的 403。</para>
    /// <para><c>GET /</c> 按 <c>offset</c> / <c>limit</c> 分页，返回 <see cref="PagedResult{T}"/>，可加 <c>unreadOnly=true</c> 只取未读；
    /// 边界见 <see cref="GetNotificationsInputDto"/>，越界抛 <see cref="ValidationException"/>（经全局异常处理为 400 字段错误）。
    /// 前缀由宿主的路由组决定；返回的路由组可继续追加约定。</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// app.MapGroup("/api/v1/notifications").MapNotifications(options =&gt; options.AccessPolicy = "App.CurrentUser");
    /// </code>
    /// </example>
    /// <param name="endpoints">路由构建器（通常是宿主的路由组）。</param>
    /// <param name="configure">授权口径。</param>
    /// <returns>承载这组端点的路由组。</returns>
    public static RouteGroupBuilder MapNotifications(
        this IEndpointRouteBuilder endpoints,
        Action<NotificationEndpointOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new NotificationEndpointOptions();
        configure(options);
        options.Validate();

        var group = endpoints.MapGroup(string.Empty).RequireAuthorization(options.AccessPolicy);

        group.MapGet(string.Empty, async (
                INotificationStore store,
                ICurrentUser currentUser,
                CancellationToken cancellationToken,
                int offset = 0,
                int limit = GetNotificationsInputDto.DefaultLimit,
                bool unreadOnly = false) =>
            {
                var input = new GetNotificationsInputDto { Offset = offset, Limit = limit, UnreadOnly = unreadOnly };
                Validator.ValidateObject(input, new ValidationContext(input), validateAllProperties: true);
                if (currentUser.Id is not { } userId)
                {
                    return PagedResult<NotificationOutputDto>.Empty;
                }

                return await store.GetByUserAsync(userId.ToString(), input, input.UnreadOnly, cancellationToken);
            })
            .WithName(NamePrefix + "GetList");

        group.MapGet("unread-count", async (INotificationStore store, ICurrentUser currentUser, CancellationToken cancellationToken)
                => currentUser.Id is { } userId ? await store.GetUnreadCountAsync(userId.ToString(), cancellationToken) : 0)
            .WithName(NamePrefix + "GetUnreadCount");

        group.MapPut("{id}/read", async (string id, INotificationStore store, ICurrentUser currentUser, CancellationToken cancellationToken) =>
            {
                await store.MarkAsReadAsync(id, RequireUser(currentUser), cancellationToken);
                return TypedResults.NoContent();
            })
            .WithName(NamePrefix + "MarkAsRead");

        group.MapPut("read-all", async (INotificationStore store, ICurrentUser currentUser, CancellationToken cancellationToken) =>
            {
                await store.MarkAllAsReadAsync(RequireUser(currentUser), cancellationToken);
                return TypedResults.NoContent();
            })
            .WithName(NamePrefix + "MarkAllAsRead");

        group.MapDelete(string.Empty, async (INotificationStore store, ICurrentUser currentUser, CancellationToken cancellationToken) =>
            {
                await store.DeleteAllAsync(RequireUser(currentUser), cancellationToken);
                return TypedResults.NoContent();
            })
            .WithName(NamePrefix + "DeleteAll");

        group.MapDelete("{id}", async (string id, INotificationStore store, ICurrentUser currentUser, CancellationToken cancellationToken) =>
            {
                await store.DeleteAsync(id, RequireUser(currentUser), cancellationToken);
                return TypedResults.NoContent();
            })
            .WithName(NamePrefix + "Delete");

        return group;
    }

    private static string RequireUser(ICurrentUser currentUser)
        => currentUser.Id?.ToString()
           ?? throw new BusinessException(NotificationErrorCodes.IdentityCannotOperate, "The current identity cannot operate on user notifications.");
}
