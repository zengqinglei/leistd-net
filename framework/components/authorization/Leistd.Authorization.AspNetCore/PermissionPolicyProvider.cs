using Microsoft.AspNetCore.Authorization;
using Leistd.Authorization;
using Microsoft.Extensions.Options;

namespace Leistd.Authorization.AspNetCore;

/// <summary>
/// 权限策略提供器：把"权限名"作为授权策略名。
/// </summary>
/// <remarks>
/// 当 <c>[Authorize(Policy = name)]</c> 的 policy 名命中
/// <see cref="IPermissionDefinitionManager"/> 中已定义的权限时，动态构建一个携带
/// <see cref="PermissionRequirement"/> 的策略；否则回退到默认策略提供器（兼容
/// <c>[Authorize]</c>、<c>[Authorize(Roles=...)]</c> 及显式注册的命名策略，如 "SuperAdmin"）。
///
/// 策略名可以用 <c>|</c> 连接多个权限名表示"任一满足"，如
/// <c>[Authorize(Policy = "App.Permissions|App.Roles.ManagePermissions")]</c>。
/// 仅当每一段都是已定义权限时才按权限策略处理，避免把恰好含 <c>|</c> 的自定义策略名误判。
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

    public async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        // 显式注册的同名策略优先：宿主可能注册了一个更严格的同名策略（例如在权限之外再要求
        // MFA 或特定 Claim），动态生成的权限策略不得把它盖掉。
        var registered = await _fallbackPolicyProvider.GetPolicyAsync(policyName);
        if (registered != null)
            return registered;

        var names = policyName.Split(PermissionPolicyNames.AnyOfSeparator, StringSplitOptions.TrimEntries);

        // 多段时要求每一段都已定义：只要有一段不是权限名，就不按权限策略处理。
        if (!Array.TrueForAll(names, name => _permissionDefinitionManager.GetOrNull(name) != null))
            return null;

        return new AuthorizationPolicyBuilder()
            .AddRequirements(new PermissionRequirement(names))
            .Build();
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync()
        => _fallbackPolicyProvider.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync()
        => _fallbackPolicyProvider.GetFallbackPolicyAsync();
}

