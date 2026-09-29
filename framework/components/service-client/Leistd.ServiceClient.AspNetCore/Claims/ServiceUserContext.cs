using System.Security.Claims;
using Leistd.Security.Claims;
using Leistd.ServiceClient.AspNetCore.Options;
using Microsoft.AspNetCore.Http;

namespace Leistd.ServiceClient.AspNetCore.Claims;

// 供 ClaimsTransformation 与中间件共用的信任判定和主体恢复逻辑。
internal static class ServiceUserContext
{
    internal const string PreferredUsernameClaimType = "preferred_username";
    private const string ScopeClaimType = "scope";
    private const string OpenIddictScopeClaimType = "oi_scp";

    internal static bool IsEnriched(ClaimsPrincipal principal, ServiceUserContextOptions options) =>
        principal.Identities.Any(identity =>
            string.Equals(identity.AuthenticationType, options.AuthenticationType, StringComparison.Ordinal));

    // 受信调用方必须经过认证，且 client_id、机器主体 sub 和可选 scope 一致。
    internal static bool IsTrustedServiceCall(ClaimsPrincipal user, ServiceUserContextOptions options, ClaimTypeOptions claimTypes)
    {
        // 有意只看第一个身份，不用 HasAuthenticatedIdentity：这里判定的是"调用方是不是受信的机器身份"，
        // 发生在还原被代表用户之前，机器令牌就是那个身份。按任一身份判定会让别的身份替机器身份过关，从严才是失败关闭
        if (user.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var clientId = user.FindFirst(CustomClaimTypes.ClientId)?.Value;
        if (string.IsNullOrEmpty(clientId))
        {
            return false;
        }

        if (!ClientSubject.Matches(claimTypes.FindUserId(user), clientId))
        {
            return false;
        }

        return string.IsNullOrEmpty(options.RequiredScope) || HasScope(user, options.RequiredScope);
    }

    // 恢复身份置于首位供 ICurrentUser 解析，并保留原 client 身份供 ICurrentClient 使用。
    // 租户可独立于用户恢复；没有任何转发上下文时返回 null。
    internal static ClaimsPrincipal? TryRestore(
        ClaimsPrincipal principal, IHeaderDictionary headers, ServiceUserContextOptions options, ClaimTypeOptions claimTypes)
    {
        var claims = new List<Claim>();

        var userId = headers[options.UserIdHeader].FirstOrDefault();
        if (!string.IsNullOrEmpty(userId))
        {
            claims.Add(new Claim(claimTypes.UserIds[0], userId));

            var userName = headers[options.UsernameHeader].FirstOrDefault();
            if (!string.IsNullOrEmpty(userName))
            {
                claims.Add(new Claim(PreferredUsernameClaimType, Uri.UnescapeDataString(userName)));
            }

            // 扩展属性只能附着于被代表的用户。
            foreach (var (headerName, claimType) in options.HeaderClaimMap)
            {
                var value = headers[headerName].FirstOrDefault();
                if (!string.IsNullOrEmpty(value))
                {
                    claims.Add(new Claim(claimType, Uri.UnescapeDataString(value)));
                }
            }
        }

        // 租户独立恢复，使数据过滤落在正确分区。写入与读取同一个 claim 类型（ClaimTypeOptions.TenantId）。
        var restoresTenant = false;
        if (!string.IsNullOrEmpty(options.TenantIdHeader))
        {
            var tenantId = headers[options.TenantIdHeader].FirstOrDefault();
            if (!string.IsNullOrEmpty(tenantId))
            {
                claims.Add(new Claim(claimTypes.TenantId, tenantId));
                restoresTenant = true;
            }
        }

        if (claims.Count == 0)
        {
            return null;
        }

        var userIdentity = new ClaimsIdentity(claims, options.AuthenticationType);
        var restored = new ClaimsPrincipal(userIdentity);
        // 代表的租户取代调用方身份上的租户声明：两条并存时主体的租户非法，请求被整体拒绝
        restored.AddIdentities(restoresTenant
            ? principal.Identities.Select(identity => WithoutClaims(identity, claimTypes.TenantId))
            : principal.Identities);
        return restored;
    }

    private static ClaimsIdentity WithoutClaims(ClaimsIdentity identity, string claimType)
    {
        if (!identity.HasClaim(claim => claim.Type == claimType))
        {
            return identity;
        }

        var copy = identity.Clone();
        foreach (var claim in copy.FindAll(claimType).ToList())
        {
            copy.RemoveClaim(claim);
        }

        return copy;
    }

    private static bool HasScope(ClaimsPrincipal user, string requiredScope)
    {
        foreach (var claim in user.FindAll(ScopeClaimType))
        {
            if (claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains(requiredScope, StringComparer.Ordinal))
            {
                return true;
            }
        }

        return user.FindAll(OpenIddictScopeClaimType)
            .Any(claim => string.Equals(claim.Value, requiredScope, StringComparison.Ordinal));
    }
}
