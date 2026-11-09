using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using Leistd.MultiTenancy.Context;
using Leistd.MultiTenancy.Stores;
using Leistd.Settings.Definitions;
using Leistd.Settings.Dtos;
using Leistd.Settings.Stores;
using Leistd.UnitOfWork;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Leistd.Settings.AspNetCore.Endpoints;

/// <summary>共享设置的 HTTP 端点：其他服务以机器身份按租户读取。</summary>
public static class SharedSettingEndpoints
{
    /// <summary>读取端点名。</summary>
    public const string GetName = SettingEndpoints.NamePrefix + "GetShared";

    // 响应体的序列化约定：固定为 Web 默认值，不随宿主的 JSON 配置变化，ETag 与读取端的解析因此稳定
    internal static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>映射 <c>GET /{tenant}</c>：返回该租户全部共享设置的生效值，带强 ETag。</summary>
    /// <remarks>
    /// <para>只含 <see cref="ISettingDefinition.IsShared"/> 的设置，值为该租户层的值，没有时为代码默认值（含部署基线），不含个人覆盖。
    /// <paramref name="endpoints"/> 下的 <c>tenant</c> 段为 <see cref="SharedSettingsDto.HostSegment"/> 或租户 Id；机器调用不带环境租户，所以租户在路径里显式给出。</para>
    /// <para>注册了 <see cref="ITenantStore"/> 时租户须存在且启用，否则 404；没有注册多租户时只接受 <see cref="SharedSettingsDto.HostSegment"/>。
    /// <c>If-None-Match</c> 命中时回 304、不发响应体。</para>
    /// <para>只要求 <see cref="SharedSettingEndpointOptions.Policy"/>，不叠加宿主默认策略；前缀由宿主的路由组决定，通常放在内部路径下。</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// app.MapGroup("/api/v1/internal/shared-settings").MapSharedSettings(options =&gt; options.Policy = "App.SharedSettings.Read");
    /// </code>
    /// </example>
    /// <param name="endpoints">路由构建器（通常是宿主的路由组）。</param>
    /// <param name="configure">授权口径。</param>
    /// <returns>承载端点的路由组。</returns>
    public static RouteGroupBuilder MapSharedSettings(
        this IEndpointRouteBuilder endpoints,
        Action<SharedSettingEndpointOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new SharedSettingEndpointOptions();
        configure(options);
        options.Validate();

        var group = endpoints.MapGroup(string.Empty);
        group.MapGet("{tenant}", async Task<IResult> (
                string tenant,
                HttpContext httpContext,
                ISettingDefinitionManager definitions,
                CancellationToken cancellationToken) =>
            {
                var services = httpContext.RequestServices;
                var currentTenant = services.GetService<ICurrentTenant>();
                Guid? tenantId;
                string? tenantName = null;
                if (string.Equals(tenant, SharedSettingsDto.HostSegment, StringComparison.Ordinal))
                {
                    tenantId = null;
                }
                else if (currentTenant is not null && Guid.TryParse(tenant, out var id))
                {
                    if (services.GetService<ITenantStore>() is { } tenantStore)
                    {
                        if (await tenantStore.FindAsync(id, cancellationToken) is not { IsActive: true } record)
                        {
                            return TypedResults.NotFound();
                        }

                        tenantName = record.Name;
                    }

                    tenantId = id;
                }
                else
                {
                    return TypedResults.NotFound();
                }

                var body = await ReadAsync(services, currentTenant, tenantId, tenantName, definitions, cancellationToken);
                var etag = new EntityTagHeaderValue($"\"{Base64Url.EncodeToString(SHA256.HashData(body).AsSpan(0, 16))}\"");
                httpContext.Response.GetTypedHeaders().ETag = etag;
                if (httpContext.Request.GetTypedHeaders().IfNoneMatch is { Count: > 0 } candidates
                    && candidates.Any(candidate => candidate.Equals(EntityTagHeaderValue.Any) || candidate.Compare(etag, useStrongComparison: false)))
                {
                    return TypedResults.StatusCode(StatusCodes.Status304NotModified);
                }

                return TypedResults.Bytes(body, "application/json");
            })
            .WithName(GetName)
            .RequireAuthorization(options.Policy);

        return group;
    }

    // 切换与工作单元建在这里：存储经当前工作单元读，连接归属在工作单元开启时决定，要在切到目标租户之后开新的。
    // 共享设置不会有用户层、进程级或密文（定义校验保证），存储的租户值 ?? 代码默认值就是完整的回落
    private static async Task<byte[]> ReadAsync(
        IServiceProvider services,
        ICurrentTenant? currentTenant,
        Guid? tenantId,
        string? tenantName,
        ISettingDefinitionManager definitions,
        CancellationToken cancellationToken)
    {
        using var tenantScope = currentTenant?.Change(tenantId, tenantName);
        using var unitOfWork = services.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var stored = await services.GetRequiredService<ISettingStore>().GetAllAsync(SettingScopes.Tenant, userId: null, cancellationToken);
        // 按名称排序：同一组值总是同一份响应体、同一个 ETag
        var values = new SortedDictionary<string, string?>(StringComparer.Ordinal);
        foreach (var definition in definitions.GetAll().Where(definition => definition.IsShared))
        {
            values[definition.Name] = stored.TryGetValue(definition.Name, out var value) ? value : definition.DefaultValue;
        }

        return JsonSerializer.SerializeToUtf8Bytes(new SharedSettingsDto(values), SerializerOptions);
    }
}
