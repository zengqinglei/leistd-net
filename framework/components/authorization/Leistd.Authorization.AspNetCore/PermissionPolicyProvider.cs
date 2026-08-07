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

    /// <summary>多权限策略名的分隔符，表示"任一满足"。</summary>
    public const char AnyOfSeparator = '|';

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        var names = policyName.Split(AnyOfSeparator, StringSplitOptions.TrimEntries);

        // 命中已定义权限 → 视为权限策略，且尚未被显式注册同名策略时由本提供器构建。
        // 多段时要求每一段都已定义：只要有一段不是权限名，整个策略名就交回默认提供器。
        if (Array.TrueForAll(names, name => _permissionDefinitionManager.GetOrNull(name) != null))
        {
            var policy = new AuthorizationPolicyBuilder()
                .AddRequirements(new PermissionRequirement(names))
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

