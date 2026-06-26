using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Leistd.Ddd.Application.Permission;

/// <summary>
/// 权限策略提供器：把"权限名"当作授权策略（ABP 风格）。
/// </summary>
/// <remarks>
/// 当 <c>[Authorize(Policy = name)]</c> 的 policy 名命中
/// <see cref="IPermissionDefinitionManager"/> 中已定义的权限时，动态构建一个携带
/// <see cref="PermissionRequirement"/> 的策略；否则回退到默认策略提供器（兼容
/// <c>[Authorize]</c>、<c>[Authorize(Roles=...)]</c> 及显式注册的命名策略，如 "SuperAdmin"）。
/// </remarks>
public class PermissionPolicyProvider : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallbackPolicyProvider;
    private readonly IPermissionDefinitionManager _permissionDefinitionManager;

    public PermissionPolicyProvider(
        IOptions<AuthorizationOptions> options,
        IPermissionDefinitionManager permissionDefinitionManager)
    {
        _fallbackPolicyProvider = new DefaultAuthorizationPolicyProvider(options);
        _permissionDefinitionManager = permissionDefinitionManager;
    }

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        // 命中已定义权限 → 视为权限策略，且尚未被显式注册同名策略时由本提供器构建
        if (_permissionDefinitionManager.GetOrNull(policyName) != null)
        {
            var policy = new AuthorizationPolicyBuilder()
                .AddRequirements(new PermissionRequirement(policyName))
                .Build();

            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        return _fallbackPolicyProvider.GetPolicyAsync(policyName);
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync()
        => _fallbackPolicyProvider.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync()
        => _fallbackPolicyProvider.GetFallbackPolicyAsync();
}
