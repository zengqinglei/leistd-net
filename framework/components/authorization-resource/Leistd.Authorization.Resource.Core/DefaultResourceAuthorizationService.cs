using Microsoft.Extensions.DependencyInjection;

namespace Leistd.Authorization.Resource;

/// <summary>
/// 默认资源实例授权服务。
/// </summary>
/// <remarks>
/// 组合两类来源：领域规则处理器 <see cref="IResourceAuthorizationHandler{TResource}"/>
/// 与资源 ACL <see cref="IResourceGrantStore"/>。任一来源拒绝即拒绝，
/// 否则任一来源允许即允许，全部无结论时默认拒绝。
/// 超级管理员旁路资源实例授权，与功能权限保持一致的口径。
/// </remarks>
public class DefaultResourceAuthorizationService(
    IPermissionSubjectProvider subjectProvider,
    IServiceProvider serviceProvider,
    IResourceGrantStore? resourceGrantStore = null) : IResourceAuthorizationService
{
    public Task<bool> IsGrantedAsync<TResource>(
        TResource resource,
        string operation,
        CancellationToken cancellationToken = default)
        where TResource : IAuthorizableResource
        => IsGrantedAsync(resource, resource.ResourceName, resource.ResourceKey, operation, cancellationToken);

    public async Task<bool> IsGrantedAsync<TResource>(
        TResource resource,
        string resourceName,
        string resourceKey,
        string operation,
        CancellationToken cancellationToken = default)
    {
        if (resource == null)
            return false;

        if (string.IsNullOrWhiteSpace(resourceName) ||
            string.IsNullOrWhiteSpace(resourceKey) ||
            string.IsNullOrWhiteSpace(operation))
        {
            return false;
        }

        var subject = await subjectProvider.GetCurrentSubjectAsync(cancellationToken);
        if (subject == null)
            return false;

        var context = new ResourceAuthorizationContext<TResource>(
            subject,
            resourceName,
            resourceKey,
            operation,
            resource);

        foreach (var handler in serviceProvider.GetServices<IResourceAuthorizationHandler<TResource>>())
        {
            await handler.HandleAsync(context, cancellationToken);
        }

        // 领域规则的拒绝先于一切，超级管理员也不例外：那类规则表达的是资源状态本身不允许
        // 执行该动作（已归档的订单谁都不能删），不是"谁有没有权限"。旁路它等于让文档说谎。
        if (context.Decision == ResourceAuthorizationDecision.Denied)
            return false;

        // 超管旁路的是授权侧的判定——RBAC、数据范围、ACL 缺失与默认拒绝，
        // 而不是上面那条领域不变量。
        if (subject.IsSuperAdmin)
            return true;

        if (resourceGrantStore != null)
        {
            var acl = await resourceGrantStore.GetEffectiveGrantsAsync(
                resourceName,
                resourceKey,
                subject.UserId,
                subject.RoleIds,
                cancellationToken);

            if (acl.TryGetValue(operation, out var effect))
            {
                // 只认明确的 Granted，其余一律拒绝。
                // 写成"是 Prohibited 就拒、否则放行"会让任何非法值 fail-open——
                // (ResourceGrantEffect)0、越界数值、自定义 Store 返回的损坏值都会被当成允许。
                // 授权判定必须 fail-closed：读不懂的东西一律当作没有授予。
                if (effect != ResourceGrantEffect.Granted)
                    return false;

                context.Allow();
            }
        }

        return context.Decision == ResourceAuthorizationDecision.Allowed;
    }
}
