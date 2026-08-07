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

        if (subject.IsSuperAdmin)
            return true;

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

        if (context.Decision == ResourceAuthorizationDecision.Denied)
            return false;

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
                if (effect == PermissionGrantEffect.Prohibited)
                    return false;

                context.Allow();
            }
        }

        return context.Decision == ResourceAuthorizationDecision.Allowed;
    }
}
