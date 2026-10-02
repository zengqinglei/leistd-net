using Leistd.Authorization.Resource.Grants;
using Leistd.Authorization.Subjects;
using Leistd.MultiTenancy.Context;
using Leistd.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Leistd.Authorization.Resource.AspNetCore.Operations;

// 资源 ACL 与超级管理员的判定，作为官方授权管线里的一个处理器：
// - 明确拒绝调用 Fail()：官方语义下任一 Fail 即失败，业务处理器的 Succeed 推翻不了它；
// - 明确授予、超级管理员调用 Succeed()：业务处理器的 Fail（资源状态本身不允许，如已归档）仍然生效；
// - 没有授予记录时什么都不做，交给业务处理器；全部无结论时官方默认拒绝。
// 主体取 context.User（被授权的主体），不回落到环境里的当前用户。
//
// ACL 按当前租户读取，因此主体的租户规则与功能权限检查器一致，且先于超管旁路：
// 租户声明非法（如两份用户凭据被拼成一个主体）一律失败关闭；显式传入的其他主体还须属于当前租户，
// 否则它的标识会被拿到别的租户的 ACL 里去比对。当前主体只校验合法性——宿主主体显式切入租户是正当用法。
internal sealed class ResourceGrantAuthorizationHandler(
    IPermissionSubjectProvider subjectProvider,
    ICurrentPrincipalAccessor principalAccessor,
    IOptions<ClaimTypeOptions> claimTypes,
    ICurrentTenant? currentTenant = null,
    IResourceGrantStore? grantStore = null) : AuthorizationHandler<ResourceOperationRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ResourceOperationRequirement requirement)
    {
        if (!BelongsToThisEvaluation(context.User))
        {
            context.Fail();
            return;
        }

        var subject = await subjectProvider.GetSubjectAsync(context.User);
        if (subject is null)
        {
            return;
        }

        if (subject.IsSuperAdmin)
        {
            context.Succeed(requirement);
            return;
        }

        if (grantStore is null)
        {
            return;
        }

        var grants = await grantStore.GetEffectiveGrantsAsync(
            requirement.ResourceName, requirement.ResourceKey, subject.UserId, subject.RoleIds);
        if (!grants.TryGetValue(requirement.Name, out var effect))
        {
            return;
        }

        // 仅明确的 Granted 允许，未知或损坏的授予值一律按拒绝处理
        if (effect == ResourceGrantEffect.Granted)
        {
            context.Succeed(requirement);
        }
        else
        {
            context.Fail();
        }
    }

    private bool BelongsToThisEvaluation(System.Security.Claims.ClaimsPrincipal principal)
    {
        var tenant = claimTypes.Value.ReadTenant(principal);
        if (!tenant.IsValid)
        {
            return false;
        }

        var isCurrent = principalAccessor.Principal is { } current && ReferenceEquals(current, principal);
        return isCurrent || currentTenant is null || tenant.TenantId == currentTenant.Id;
    }
}
