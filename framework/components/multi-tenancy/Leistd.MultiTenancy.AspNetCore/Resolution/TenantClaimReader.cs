using System.Security.Claims;
using Leistd.MultiTenancy.Exceptions;

namespace Leistd.MultiTenancy.AspNetCore.Resolution;

// claim → 租户 的唯一判定实现。HTTP 解析链与 Hub 等非 HTTP 入口共用这一份：
// 两处各写一遍必然漂移，而这条判定同时决定"谁是宿主"与"重复声明是否失败关闭"。
internal static class TenantClaimReader
{
    // 从主体读租户标识：返回 null 表示宿主（无租户声明）；
    // 多个租户声明抛 AmbiguousTenantClaimException。
    internal static string? Read(ClaimsPrincipal principal, string tenantClaimType)
    {
        var claims = principal.FindAll(tenantClaimType).ToList();

        return claims.Count switch
        {
            0 => null,
            1 => claims[0].Value,
            // 重复声明失败关闭，避免多种身份来源被错误合并。
            _ => throw new AmbiguousTenantClaimException(tenantClaimType, claims.Count)
        };
    }
}
