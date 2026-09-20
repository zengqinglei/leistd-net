using Leistd.Authorization.Constants;

namespace Leistd.Authorization.AspNetCore.Endpoints;

/// <summary>
/// 权限管理端点的授权口径与开放的主体类型。
/// </summary>
/// <remarks>
/// 组件不内置默认策略、也不在路由组上套宿主默认策略：谁能读自己的权限、谁能看权限目录、谁能改谁的授予
/// 都是宿主的权限词汇。漏配 <see cref="CurrentPolicy"/>、<see cref="DefinitionsPolicy"/>
/// 或某个主体类型的策略为空时，<c>MapPermissionManagement</c> 在映射时就抛出。
/// </remarks>
public sealed class PermissionManagementEndpointOptions
{
    /// <summary>
    /// 读取当前主体已获授权所需的授权策略名。
    /// </summary>
    /// <remarks>该端点只返回当前主体自己的授权，宿主通常给一条"已登录的交互用户"策略。</remarks>
    public string CurrentPolicy { get; set; } = string.Empty;

    /// <summary>读取权限定义树所需的授权策略名。</summary>
    public string DefinitionsPolicy { get; set; } = string.Empty;

    /// <summary>
    /// 开放授予管理的主体类型 → 所需授权策略名；未列出的类型不映射端点。
    /// </summary>
    /// <remarks>键取 <see cref="PermissionGrantProviderNames"/>：<c>Role</c> 映射到 <c>grants/roles/{key}</c>，<c>User</c> 映射到 <c>grants/users/{key}</c>。</remarks>
    public IDictionary<string, string> GrantPolicies { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    // 策略名为空或主体类型不受支持都抛 ArgumentException；支持范围由端点侧传入，选项不反向依赖路由表
    internal void Validate(IReadOnlyCollection<string> supportedProviders)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(CurrentPolicy);
        ArgumentException.ThrowIfNullOrWhiteSpace(DefinitionsPolicy);

        foreach (var (providerName, policy) in GrantPolicies)
        {
            if (!supportedProviders.Contains(providerName))
            {
                throw new ArgumentException(
                    $"Permission grant provider '{providerName}' is not supported; use {string.Join(" or ", supportedProviders)}.",
                    nameof(GrantPolicies));
            }

            if (string.IsNullOrWhiteSpace(policy))
            {
                throw new ArgumentException(
                    $"The grant policy for '{providerName}' must not be empty.",
                    nameof(GrantPolicies));
            }
        }
    }
}
