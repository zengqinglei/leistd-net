using System.Security.Claims;
using Leistd.Security.Claims;
using Leistd.ServiceClient.AspNetCore.Options;
using Microsoft.AspNetCore.Http;

namespace Leistd.ServiceClient.AspNetCore.Claims;

/// <summary>
/// 服务用户上下文的信任判定与主体恢复逻辑（供 ClaimsTransformation 与中间件共用）。
/// </summary>
internal static class ServiceUserContext
{
    internal const string SubjectClaimType = "sub";
    internal const string PreferredUsernameClaimType = "preferred_username";
    private const string ScopeClaimType = "scope";
    private const string OpenIddictScopeClaimType = "oi_scp";

    /// <summary>
    /// 主体是否已经过服务用户上下文恢复（含 AuthenticationType 标记的身份）。
    /// </summary>
    internal static bool IsEnriched(ClaimsPrincipal principal, ServiceUserContextOptions options) =>
        principal.Identities.Any(identity =>
            string.Equals(identity.AuthenticationType, options.AuthenticationType, StringComparison.Ordinal));

    /// <summary>
    /// 主体是否是受信的服务调用方：已认证 + 含 <c>client_id</c> claim +
    /// <c>sub</c> 是该 client 的机器主体（<see cref="ClientSubject"/> 契约，
    /// 即 <c>client:&lt;client_id&gt;</c>）+ 可选 RequiredScope。
    /// 用户 token 的 <c>sub</c> 是用户 Id，结构上不可能匹配。
    /// </summary>
    internal static bool IsTrustedServiceCall(ClaimsPrincipal user, ServiceUserContextOptions options)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var clientId = user.FindFirst(CustomClaimTypes.ClientId)?.Value;
        if (string.IsNullOrEmpty(clientId))
        {
            return false;
        }

        var subject = user.FindFirst(SubjectClaimType)?.Value
                      ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!ClientSubject.Matches(subject, clientId))
        {
            return false;
        }

        return string.IsNullOrEmpty(options.RequiredScope) || HasScope(user, options.RequiredScope);
    }

    /// <summary>
    /// 从请求头恢复用户/租户上下文：恢复出的身份置于首位成为主身份（<c>ICurrentUser</c> 的
    /// <c>sub</c> 解析命中用户而非 client），调用方 client 身份全部保留（<c>ICurrentClient</c> 可用）。
    /// 租户恢复独立于用户头——仅有租户上下文的服务调用（如后台任务）同样恢复
    /// <c>tenant_id</c> claim，交由多租户解析链的 Claim 贡献者定案。
    /// 请求既无用户 Id 头也无租户头（服务以自身宿主身份调用）时返回 <c>null</c>。
    /// </summary>
    internal static ClaimsPrincipal? TryRestore(
        ClaimsPrincipal principal, IHeaderDictionary headers, ServiceUserContextOptions options)
    {
        var claims = new List<Claim>();

        var userId = headers[options.UserIdHeader].FirstOrDefault();
        if (!string.IsNullOrEmpty(userId))
        {
            claims.Add(new Claim(SubjectClaimType, userId));

            var userName = headers[options.UserNameHeader].FirstOrDefault();
            if (!string.IsNullOrEmpty(userName))
            {
                claims.Add(new Claim(PreferredUsernameClaimType, Uri.UnescapeDataString(userName)));
            }

            // 额外映射跟随用户上下文：没有用户就没有"代表谁"的扩展属性
            foreach (var (headerName, claimType) in options.HeaderClaimMap)
            {
                var value = headers[headerName].FirstOrDefault();
                if (!string.IsNullOrEmpty(value))
                {
                    claims.Add(new Claim(claimType, Uri.UnescapeDataString(value)));
                }
            }
        }

        // 租户头独立恢复：使被调方的 ICurrentTenant 与数据过滤落在正确租户分区
        if (!string.IsNullOrEmpty(options.TenantIdHeader))
        {
            var tenantId = headers[options.TenantIdHeader].FirstOrDefault();
            if (!string.IsNullOrEmpty(tenantId))
            {
                claims.Add(new Claim(CustomClaimTypes.TenantId, tenantId));
            }
        }

        if (claims.Count == 0)
        {
            return null;
        }

        var userIdentity = new ClaimsIdentity(claims, options.AuthenticationType);
        var restored = new ClaimsPrincipal(userIdentity);
        restored.AddIdentities(principal.Identities);
        return restored;
    }

    private static bool HasScope(ClaimsPrincipal user, string requiredScope)
    {
        // 标准形态：单个 "scope" claim，空格分隔
        foreach (var claim in user.FindAll(ScopeClaimType))
        {
            if (claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains(requiredScope, StringComparer.Ordinal))
            {
                return true;
            }
        }

        // OpenIddict 私有形态：多个 "oi_scp" claim，每个一条 scope
        return user.FindAll(OpenIddictScopeClaimType)
            .Any(claim => string.Equals(claim.Value, requiredScope, StringComparison.Ordinal));
    }
}
