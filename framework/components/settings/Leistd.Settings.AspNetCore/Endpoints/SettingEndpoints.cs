using Leistd.Settings.Definitions;
using Leistd.Settings.Errors;
using Leistd.Settings.Management;
using Leistd.Settings.Resolution;
using Leistd.Settings.Stores;
using Leistd.Settings.Dtos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Leistd.Settings.AspNetCore.Endpoints;

/// <summary>
/// 设置页的 HTTP 端点。
/// </summary>
public static class SettingEndpoints
{
    /// <summary>端点名前缀，宿主按名字给个别端点追加约定时使用。</summary>
    public const string NamePrefix = "Leistd.Settings.";

    /// <summary>读取端点名。</summary>
    public const string GetName = NamePrefix + "Get";

    /// <summary>写当前用户偏好的端点名。</summary>
    public const string SetForCurrentUserName = NamePrefix + "SetForCurrentUser";

    /// <summary>写当前租户设置的端点名。</summary>
    public const string SetForCurrentTenantName = NamePrefix + "SetForCurrentTenant";

    /// <summary>
    /// 映射设置页端点：<c>GET /</c>、<c>PUT /current-user</c>、<c>PUT /current-tenant</c>。
    /// </summary>
    /// <remarks>
    /// <para>写入分成两个端点而不是一个带层级参数的端点：两者的授权要求不同——改自己的偏好走
    /// <see cref="SettingEndpointOptions.AccessPolicy"/>，改租户值走 <see cref="SettingEndpointOptions.TenantWritePolicy"/>。
    /// 合成一个会让这层差异藏进请求体。</para>
    /// <para>写入成功返回 204；取值、层级与可见性错误是带码的 400 / 403 / 404。
    /// 前缀由宿主的路由组决定；返回的路由组可继续追加约定。</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// app.MapGroup("/api/v1/settings").MapSettings(options =&gt;
    /// {
    ///     options.AccessPolicy = "App.CurrentUser";
    ///     options.TenantWritePolicy = "App.Settings";
    /// });
    /// </code>
    /// </example>
    /// <param name="endpoints">路由构建器（通常是宿主的路由组）。</param>
    /// <param name="configure">授权口径。</param>
    /// <returns>承载这组端点的路由组。</returns>
    public static RouteGroupBuilder MapSettings(
        this IEndpointRouteBuilder endpoints,
        Action<SettingEndpointOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new SettingEndpointOptions();
        configure(options);
        options.Validate();

        var group = endpoints.MapGroup(string.Empty);

        group.MapGet(string.Empty, (ISettingManagementService service, CancellationToken cancellationToken)
                => service.GetAsync(cancellationToken))
            .WithName(GetName)
            .RequireAuthorization(options.AccessPolicy);

        group.MapPut("current-user", async (SetSettingInputDto input, ISettingManagementService service, CancellationToken cancellationToken) =>
            {
                await service.SetForCurrentUserAsync(input, cancellationToken);
                return TypedResults.NoContent();
            })
            .WithName(SetForCurrentUserName)
            .RequireAuthorization(options.AccessPolicy);

        group.MapPut("current-tenant", async (SetSettingInputDto input, ISettingManagementService service, CancellationToken cancellationToken) =>
            {
                await service.SetForCurrentTenantAsync(input, cancellationToken);
                return TypedResults.NoContent();
            })
            .WithName(SetForCurrentTenantName)
            .RequireAuthorization(options.TenantWritePolicy);

        return group;
    }
}
