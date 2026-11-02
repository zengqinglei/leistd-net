using CompanyName.ProjectName.Api.Auth.Authorization;
#if (LocalIdentity && IncludeMultiTenancy)
using CompanyName.ProjectName.Application.Tenants.Dtos;
#if (OpenIddictServer)
using CompanyName.ProjectName.Application.TenantConnections.Constants;
#endif
using Leistd.MultiTenancy.AspNetCore.Endpoints;
#endif
using CompanyName.ProjectName.Application.OperationRecords.Provider;
using CompanyName.ProjectName.Application.Permissions.Provider;
using CompanyName.ProjectName.Application.Settings.Provider;
using Leistd.Authorization.AspNetCore.Endpoints;
using Leistd.Authorization.Constants;
#if (IncludeNotifications)
using Leistd.Notifications.AspNetCore.Endpoints;
#endif
using Leistd.OperationRecords.AspNetCore.Attributes;
#if (IncludeOperationRecords)
using Leistd.OperationRecords.AspNetCore.Endpoints;
#endif
using Leistd.Settings.AspNetCore.Endpoints;
#if (IncludeWebhooks)
using Leistd.OperationRecords.Models;
using Leistd.OperationRecords.Recording;
using Leistd.UnitOfWork;
using Leistd.Webhooks.AspNetCore.Endpoints;
using Leistd.Webhooks.Dtos;
#endif

namespace CompanyName.ProjectName.Api.Hosting;

/// <summary>组件自带的 HTTP 端点：本项目只给前缀、授权策略与个别端点的附加元数据。</summary>
/// <remarks>
/// <para>业务动作（发信测试、模拟登录）由控制器提供，与组件端点共用前缀，路由不重叠。</para>
/// <para>个别端点要追加元数据时按组件公开的端点名定位（<see cref="WithMetadataOn{TBuilder}"/>），
/// 不改组件、不复制端点。</para>
/// </remarks>
public static class ComponentEndpoints
{
    /// <summary>映射本项目用到的组件端点。</summary>
    /// <param name="app">应用。</param>
    public static WebApplication MapComponentEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1");

        api.MapGroup("settings")
            .MapSettings(options =>
            {
                options.AccessPolicy = ApiPolicies.CurrentUser;
                options.TenantWritePolicy = PermissionConstant.Settings.Default;
                foreach (var (group, permission) in SettingGroupPermissions.All)
                {
                    options.GroupPolicies[group] = permission;
                }
            })
#if (LocalIdentity)
            // 界面启动就要读：受限会话（要求两步验证而本人未启用）也得能渲染出两步验证设置页
            .WithMetadataOn(SettingEndpoints.GetName, new AllowDuringTwoFactorSetupAttribute())
#endif
            ;

        api.MapGroup("permissions")
            .MapPermissionManagement(options =>
            {
                options.CurrentPolicy = ApiPolicies.CurrentUser;
                // 权限树只为授予而读，能配置角色权限的人才需要它；不另设「查看权限目录」权限
                options.DefinitionsPolicy = PermissionConstant.Roles.ManagePermissions;
                options.GrantPolicies[PermissionGrantProviderNames.Role] = PermissionConstant.Roles.ManagePermissions;
            })
#if (LocalIdentity)
            .WithMetadataOn(PermissionManagementEndpoints.GetCurrentName, new AllowDuringTwoFactorSetupAttribute())
#endif
            // 授权阶段被拒也要留痕；目标标识拼成 "Role/{roleId}"，与成功路径写下的逐字一致，按目标检索才查得全
            .WithMetadataOn(
                PermissionManagementEndpoints.ReplaceGrantsName(PermissionGrantProviderNames.Role),
                new OperationRecordActionAttribute(OperationRecordActions.PermissionGrantsReplaced, "providerKey")
                {
                    TargetIdPrefix = PermissionGrantProviderNames.Role + "/"
                });

#if (IncludeOperationRecords)
        // 导出与查看分开授权：整批带离系统的影响面与在线翻页不是一个量级；导出本身也留痕
        api.MapGroup("operation-records").MapOperationRecords(options =>
        {
            options.ReadPolicy = PermissionConstant.OperationRecords.Default;
            options.ExportPolicy = PermissionConstant.OperationRecords.Export;
            options.ExportAction = OperationRecordActions.OperationRecordsExported;
        });
#endif
#if (IncludeWebhooks)

        // 订阅的写操作留痕：被拒与业务拒绝由端点上的动作码补记失败，成功记录由过滤器与写入在同一事务里提交。
        // 测试事件与手动重发不改变"谁能收到、凭什么验签"，不记
        api.MapGroup("webhooks")
            .MapWebhooks(options =>
            {
                options.ReadPolicy = PermissionConstant.Webhooks.Default;
                options.ManagePolicy = PermissionConstant.Webhooks.Manage;
            })
            .WithMetadataOn(WebhookEndpoints.NamePrefix + "CreateSubscription",
                new OperationRecordActionAttribute(OperationRecordActions.WebhookSubscriptionCreated))
            .WithMetadataOn(WebhookEndpoints.NamePrefix + "DeleteSubscription",
                new OperationRecordActionAttribute(OperationRecordActions.WebhookSubscriptionDeleted, "id"))
            .WithMetadataOn(WebhookEndpoints.NamePrefix + "RotateSecret",
                new OperationRecordActionAttribute(OperationRecordActions.WebhookSubscriptionSecretRotated, "id"))
            .WithMetadataOn(WebhookEndpoints.NamePrefix + "EnableSubscription",
                new OperationRecordActionAttribute(OperationRecordActions.WebhookSubscriptionEnabled, "id"))
            .WithMetadataOn(WebhookEndpoints.NamePrefix + "DisableSubscription",
                new OperationRecordActionAttribute(OperationRecordActions.WebhookSubscriptionDisabled, "id"))
            .AddEndpointFilter(RecordWebhookSubscriptionChangeAsync);
#endif
#if (IncludeNotifications)

        api.MapGroup("notifications").MapNotifications(options => options.AccessPolicy = ApiPolicies.CurrentUser);
#endif
#if (LocalIdentity && IncludeMultiTenancy)

        // 租户管理是宿主侧能力：权限声明为 Host 侧别，租户上下文内任何主体都无法通过检查
        api.MapGroup("tenants").MapTenantManagement<CreateTenantWithAdminInputDto>(options =>
        {
            options.ReadPolicy = PermissionConstant.Tenants.Default;
            options.CreatePolicy = PermissionConstant.Tenants.Create;
            options.UpdatePolicy = PermissionConstant.Tenants.Update;
            options.DeletePolicy = PermissionConstant.Tenants.Delete;
        });

        api.MapGroup("tenant-connections").MapTenantConnections(options =>
        {
            options.ManagePolicy = PermissionConstant.Tenants.Update;
#if (OpenIddictServer)
            // 机器端点只在签发令牌的形态下映射：不签发令牌时不可能存在机器主体，永远无人可用的内部端点只是攻击面
            options.RuntimeReadPolicy = TenantConnectionPolicies.RuntimeRead;
            options.MigrationReadPolicy = TenantConnectionPolicies.MigrationRead;
#endif
        });
#endif

        return app;
    }

#if (IncludeWebhooks)
    // 组件的管理用例不发布订阅变更事件，成功记录在这里补：端点声明了动作码才记。写入并入这里开的工作单元，
    // 用例被拒或失败时连同记录一起回滚；目标名取用例返回的订阅，删除没有返回值，只记标识
    private static async ValueTask<object?> RecordWebhookSubscriptionChangeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        if (httpContext.GetEndpoint()?.Metadata.GetMetadata<OperationRecordActionAttribute>() is not { } declared)
        {
            return await next(context);
        }

        var services = httpContext.RequestServices;
        using var unitOfWork = services.GetRequiredService<IUnitOfWorkManager>().Begin();
        var result = await next(context);
        var target = result switch
        {
            WebhookSubscriptionSecretOutputDto created => OperationTarget.For(created.Subscription.Id, created.Subscription.Name),
            WebhookSubscriptionOutputDto subscription => OperationTarget.For(subscription.Id, subscription.Name),
            // 路由带 guid 约束；按 Guid 归一，与被拒路径记下的标识写法一致
            _ => OperationTarget.For(Guid.Parse(httpContext.Request.RouteValues["id"]!.ToString()!))
        };
        await services.GetRequiredService<IOperationRecorder>().RecordSucceededAsync(
            declared.Action, target, PermissionConstant.Webhooks.Manage, httpContext.RequestAborted);
        await unitOfWork.CompleteAsync(httpContext.RequestAborted);
        return result;
    }

#endif
    // 按组件公开的端点名给个别端点追加元数据；Finally 在端点自己的约定之后执行，端点名此时已经在元数据里
    private static TBuilder WithMetadataOn<TBuilder>(this TBuilder builder, string endpointName, params object[] metadata)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.Finally(endpoint =>
        {
            if (endpoint.Metadata.OfType<IEndpointNameMetadata>().Any(name => name.EndpointName == endpointName))
            {
                foreach (var item in metadata)
                {
                    endpoint.Metadata.Add(item);
                }
            }
        });
        return builder;
    }
}
