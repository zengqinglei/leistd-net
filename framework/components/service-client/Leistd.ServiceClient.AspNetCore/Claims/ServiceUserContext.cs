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
    /// 主体是否是受信的服务调用方：已认证 + 含 <c>client_id</c> claim + <c>sub == client_id</c>
    /// （client credentials 形态；用户 token 的 <c>sub</c> 是用户 Id，不满足）+ 可选 RequiredScope。
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
        if (!string.Equals(subject, clientId, StringComparison.Ordinal))
        {
            return false;
        }

        return string.IsNullOrEmpty(options.RequiredScope) || HasScope(user, options.RequiredScope);
    }

    /// <summary>
    /// 从请求头恢复用户主体：用户身份置于首位成为主身份（<c>ICurrentUser</c> 的 <c>sub</c>
    /// 解析命中用户而非 client），调用方 client 身份全部保留（<c>ICurrentClient</c> 可用）。
    /// 请求无用户 Id 头（服务以自身身份调用）时返回 <c>null</c>。
    /// </summary>
    internal static ClaimsPrincipal? TryRestore(
        ClaimsPrincipal principal, IHeaderDictionary headers, ServiceUserContextOptions options)
    {
        var userId = headers[options.UserIdHeader].FirstOrDefault();
        if (string.IsNullOrEmpty(userId))
        {
            return null;
        }

        var claims = new List<Claim> { new(SubjectClaimType, userId) };

        var userName = headers[options.UserNameHeader].FirstOrDefault();
        if (!string.IsNullOrEmpty(userName))
        {
            claims.Add(new Claim(PreferredUsernameClaimType, Uri.UnescapeDataString(userName)));
        }

        foreach (var (headerName, claimType) in options.HeaderClaimMap)
        {
            var value = headers[headerName].FirstOrDefault();
            if (!string.IsNullOrEmpty(value))
            {
                claims.Add(new Claim(claimType, Uri.UnescapeDataString(value)));
            }
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
