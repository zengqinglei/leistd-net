using Leistd.Data.Paging;
using Leistd.MultiTenancy.Abstractions;
using Leistd.MultiTenancy.AspNetCore.Resolution;
using Leistd.MultiTenancy.Dtos;
using Leistd.MultiTenancy.Resolution;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Leistd.MultiTenancy.AspNetCore.Endpoints;

/// <summary>
/// 租户管理与租户连接的 HTTP 端点。
/// </summary>
public static class TenantManagementEndpoints
{
    /// <summary>端点名前缀，宿主按名字给个别端点追加约定时使用。</summary>
    public const string NamePrefix = "Leistd.MultiTenancy.";

    /// <summary>
    /// 映射租户管理：<c>GET /</c>、<c>GET /{id}</c>、<c>POST /</c>、<c>PUT /{id}</c>、<c>PUT /{id}/activation</c>、
    /// <c>DELETE /{id}</c>，以及匿名的 <c>GET /by-name/{name}</c> 与 <c>GET /by-host</c>。
    /// </summary>
    /// <remarks>
    /// <para>创建请求体按 <typeparamref name="TCreateInput"/> 绑定：宿主派生 <see cref="CreateTenantInputDto"/>
    /// 携带开通需要的更多信息，开通器从上下文里取回。</para>
    /// <para>匿名探测只回选择租户所需的最小信息；<c>by-host</c> 只按请求主机名解析，不接受账号一类入参——
    /// 那会需要一个匿名的"这个账号属于哪些租户"查询，一份邮箱字典就能刷出租户拓扑。</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// app.MapGroup("/api/v1/tenants").MapTenantManagement&lt;CreateTenantWithAdminInputDto&gt;(options =&gt;
    /// {
    ///     options.ReadPolicy = "App.Tenants";
    ///     options.CreatePolicy = "App.Tenants.Create";
    ///     options.UpdatePolicy = "App.Tenants.Update";
    ///     options.DeletePolicy = "App.Tenants.Delete";
    /// });
    /// </code>
    /// </example>
    /// <typeparam name="TCreateInput">创建请求体类型。</typeparam>
    /// <param name="endpoints">路由构建器（通常是宿主的路由组）。</param>
    /// <param name="configure">授权口径。</param>
    /// <returns>承载这组端点的路由组。</returns>
    public static RouteGroupBuilder MapTenantManagement<TCreateInput>(
        this IEndpointRouteBuilder endpoints,
        Action<TenantManagementEndpointOptions> configure)
        where TCreateInput : CreateTenantInputDto
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new TenantManagementEndpointOptions();
        configure(options);
        options.Validate();

        // 不在组上套无参 RequireAuthorization：每个端点都已声明具名策略，再叠一层宿主默认策略
        // 等于替宿主给管理端点追加了它没要求过的主体条件（机器主体因此会被静默拒绝）。
        var group = endpoints.MapGroup(string.Empty);

        group.MapGet(string.Empty, (
                ITenantManagementService service,
                CancellationToken cancellationToken,
                int offset = 0,
                int limit = PageRequest.DefaultLimit,
                string? sorting = null,
                string? keyword = null)
                => service.GetPagedAsync(
                    new GetTenantPagedInputDto { Offset = offset, Limit = limit, Sorting = sorting, Keyword = keyword },
                    cancellationToken))
            .WithName(NamePrefix + "GetTenants")
            .RequireAuthorization(options.ReadPolicy);

        group.MapGet("{id:guid}", (Guid id, ITenantManagementService service, CancellationToken cancellationToken)
                => service.GetAsync(id, cancellationToken))
            .WithName(NamePrefix + "GetTenant")
            .RequireAuthorization(options.ReadPolicy);

        group.MapPost(string.Empty, (TCreateInput input, ITenantManagementService service, CancellationToken cancellationToken)
                => service.CreateAsync(input, cancellationToken))
            .WithName(NamePrefix + "CreateTenant")
            .RequireAuthorization(options.CreatePolicy);

        group.MapPut("{id:guid}", (Guid id, UpdateTenantInputDto input, ITenantManagementService service, CancellationToken cancellationToken)
                => service.UpdateAsync(id, input, cancellationToken))
            .WithName(NamePrefix + "UpdateTenant")
            .RequireAuthorization(options.UpdatePolicy);

        group.MapPut("{id:guid}/activation", (
                Guid id,
                UpdateTenantActivationInputDto input,
                ITenantManagementService service,
                CancellationToken cancellationToken)
                => service.SetActivationAsync(id, input, cancellationToken))
            .WithName(NamePrefix + "SetTenantActivation")
            .RequireAuthorization(options.UpdatePolicy);

        group.MapDelete("{id:guid}", async (Guid id, ITenantManagementService service, CancellationToken cancellationToken) =>
            {
                await service.DeleteAsync(id, cancellationToken);
                return TypedResults.NoContent();
            })
            .WithName(NamePrefix + "DeleteTenant")
            .RequireAuthorization(options.DeletePolicy);

        group.MapGet("by-name/{name}", async Task<IResult> (string name, ITenantManagementService service, CancellationToken cancellationToken)
                => await service.FindByNameAsync(name, cancellationToken) is { } tenant
                    ? TypedResults.Ok(tenant)
                    : TypedResults.NotFound())
            .WithName(NamePrefix + "FindTenantByName")
            .AllowAnonymous();

        group.MapGet("by-host", async (HttpContext context, ITenantManagementService service, CancellationToken cancellationToken) =>
            {
                // 复用解析链的域名贡献者，而不是在这里另写一份主机名匹配：两处各写，受管域判定迟早不一致
                var resolve = new TenantResolveContext(context.RequestServices);
                await new DomainTenantResolveContributor().ResolveAsync(resolve);

                if (resolve.TenantIdOrName is { Length: > 0 } tenantName)
                {
                    return new TenantByHostOutputDto
                    {
                        Decision = HostTenantDecision.Tenant,
                        Tenant = await service.FindByNameAsync(tenantName, cancellationToken)
                    };
                }

                // Handled 是"受管域内但不指向租户"，必须与"根本不是受管域"分开回传
                return new TenantByHostOutputDto
                {
                    Decision = resolve.Handled ? HostTenantDecision.Host : HostTenantDecision.Undecided
                };
            })
            .WithName(NamePrefix + "FindTenantByHost")
            .AllowAnonymous();

        return group;
    }

    /// <summary>用 <see cref="CreateTenantInputDto"/> 作为创建请求体映射租户管理，见 <see cref="MapTenantManagement{TCreateInput}"/>。</summary>
    /// <param name="endpoints">路由构建器（通常是宿主的路由组）。</param>
    /// <param name="configure">授权口径。</param>
    /// <returns>承载这组端点的路由组。</returns>
    public static RouteGroupBuilder MapTenantManagement(
        this IEndpointRouteBuilder endpoints,
        Action<TenantManagementEndpointOptions> configure)
        => endpoints.MapTenantManagement<CreateTenantInputDto>(configure);

    /// <summary>
    /// 映射租户连接：<c>GET /{tenantId}</c>、<c>PUT /{tenantId}/{name}</c>、<c>DELETE /{tenantId}/{name}?expectedVersion=</c>，
    /// 以及配置了策略时的机器端点 <c>GET /runtime/{tenantId}?name=</c> 与 <c>GET /migration?name=</c>。
    /// </summary>
    /// <remarks>
    /// <para>管理面只回名字与版本；机器端点按名字问、按名字答，一次只回被问到的那一条连接的明文，
    /// 不把该租户在别的服务的连接串也发出去。远端连接存储（<c>Leistd.MultiTenancy.ServiceClient</c>）按这两条路由回源。</para>
    /// <para>每个端点只挂自己的命名策略，路由组上不叠宿主的默认策略——默认策略通常要求自然人，叠上去机器令牌就进不来。
    /// 连接名不合法返回带码的 400；启用中的租户改变数据落点返回 409。</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// app.MapGroup("/api/v1/tenant-connections").MapTenantConnections(options =&gt;
    /// {
    ///     options.ManagePolicy = "App.Tenants.Update";
    ///     options.RuntimeReadPolicy = "TenantConnection.RuntimeRead";
    ///     options.MigrationReadPolicy = "TenantConnection.MigrationRead";
    /// });
    /// </code>
    /// </example>
    /// <param name="endpoints">路由构建器（通常是宿主的路由组）。</param>
    /// <param name="configure">授权口径。</param>
    /// <returns>承载这组端点的路由组。</returns>
    public static RouteGroupBuilder MapTenantConnections(
        this IEndpointRouteBuilder endpoints,
        Action<TenantConnectionEndpointOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new TenantConnectionEndpointOptions();
        configure(options);
        options.Validate();

        // 路由组上不挂默认策略：宿主的默认策略通常表达"一个自然人"，叠在机器端点上会让机器令牌永远 403。
        // 每个端点都有自己的命名策略，已经隐含"已认证"。
        var group = endpoints.MapGroup(string.Empty);

        group.MapGet("{tenantId:guid}", (Guid tenantId, ITenantConnectionManagementService service, CancellationToken cancellationToken)
                => service.GetListAsync(tenantId, cancellationToken))
            .WithName(NamePrefix + "GetTenantConnections")
            .RequireAuthorization(options.ManagePolicy);

        group.MapPut("{tenantId:guid}/{name}", (
                Guid tenantId,
                string name,
                UpsertTenantConnectionInputDto input,
                ITenantConnectionManagementService service,
                CancellationToken cancellationToken)
                => service.SetAsync(tenantId, name, input, cancellationToken))
            .WithName(NamePrefix + "SetTenantConnection")
            .RequireAuthorization(options.ManagePolicy);

        group.MapDelete("{tenantId:guid}/{name}", async (
                Guid tenantId,
                string name,
                long expectedVersion,
                ITenantConnectionManagementService service,
                CancellationToken cancellationToken) =>
            {
                await service.RemoveAsync(tenantId, name, expectedVersion, cancellationToken);
                return TypedResults.NoContent();
            })
            .WithName(NamePrefix + "RemoveTenantConnection")
            .RequireAuthorization(options.ManagePolicy);

        if (!string.IsNullOrWhiteSpace(options.RuntimeReadPolicy))
        {
            group.MapGet("runtime/{tenantId:guid}", (
                    Guid tenantId,
                    string? name,
                    ITenantConnectionManagementService service,
                    CancellationToken cancellationToken)
                    => service.GetRuntimeAsync(tenantId, name ?? string.Empty, cancellationToken))
                .WithName(NamePrefix + "GetRuntimeTenantConnection")
                .RequireAuthorization(options.RuntimeReadPolicy);
        }

        if (!string.IsNullOrWhiteSpace(options.MigrationReadPolicy))
        {
            group.MapGet("migration", (string? name, ITenantConnectionManagementService service, CancellationToken cancellationToken)
                    => service.GetMigrationListAsync(name ?? string.Empty, cancellationToken))
                .WithName(NamePrefix + "GetMigrationTenantConnections")
                .RequireAuthorization(options.MigrationReadPolicy);
        }

        return group;
    }
}
