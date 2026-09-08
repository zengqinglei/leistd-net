using System.Security.Claims;
using Leistd.Security.Claims;
using Leistd.ServiceClient.AspNetCore.Options;
using Microsoft.AspNetCore.Http;

namespace Leistd.ServiceClient.AspNetCore.Claims;

// 供 ClaimsTransformation 与中间件共用的信任判定和主体恢复逻辑。
internal static class ServiceUserContext
{
    internal const string SubjectClaimType = "sub";
    internal const string PreferredUsernameClaimType = "preferred_username";
    private const string ScopeClaimType = "scope";
    private const string OpenIddictScopeClaimType = "oi_scp";

    internal static bool IsEnriched(ClaimsPrincipal principal, ServiceUserContextOptions options) =>
        principal.Identities.Any(identity =>
            string.Equals(identity.AuthenticationType, options.AuthenticationType, StringComparison.Ordinal));

    // 受信调用方必须经过认证，且 client_id、机器主体 sub 和可选 scope 一致。
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

    // 恢复身份置于首位供 ICurrentUser 解析，并保留原 client 身份供 ICurrentClient 使用。
    // 租户可独立于用户恢复；没有任何转发上下文时返回 null。
    internal static ClaimsPrincipal? TryRestore(
        ClaimsPrincipal principal, IHeaderDictionary headers, ServiceUserContextOptions options)
    {
        var claims = new List<Claim>();

        var userId = headers[options.UserIdHeader].FirstOrDefault();
        if (!string.IsNullOrEmpty(userId))
        {
            claims.Add(new Claim(SubjectClaimType, userId));

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

        // 租户独立恢复，使数据过滤落在正确分区。
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
