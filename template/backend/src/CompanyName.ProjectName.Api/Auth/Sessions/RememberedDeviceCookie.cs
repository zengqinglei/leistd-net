#if (LocalIdentity)
using System.Security.Claims;
using CompanyName.ProjectName.Application.Auth.SignIn;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace CompanyName.ProjectName.Api.Auth.Sessions;

/// <summary>读写两步验证的记住设备票据，业务校验由登录流程按当前的账号与租户策略完成。</summary>
public sealed class RememberedDeviceCookie
{
    private const string TenantClaim = "tenant_id";
    private const string SecurityStampClaim = "security_stamp";
    private static readonly string Scheme = IdentityConstants.TwoFactorRememberMeScheme;

    public async Task<RememberedDevice?> ReadAsync(HttpContext context)
    {
        var result = await context.AuthenticateAsync(Scheme);
        if (result.Principal is not { } principal ||
            !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ||
            principal.FindFirstValue(SecurityStampClaim) is not { } stamp ||
            result.Properties?.IssuedUtc is not { } issued)
            return null;

        var tenantId = Guid.TryParse(principal.FindFirstValue(TenantClaim), out var tenant) ? tenant : (Guid?)null;
        return new RememberedDevice(userId, tenantId, stamp, issued.UtcDateTime);
    }

    public Task SignInAsync(HttpContext context, RememberedDevice device, int days)
    {
        var identity = new ClaimsIdentity(Scheme);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, device.UserId.ToString()));
        identity.AddClaim(new Claim(SecurityStampClaim, device.SecurityStamp));
        if (device.TenantId is { } tenant)
            identity.AddClaim(new Claim(TenantClaim, tenant.ToString()));

        var issued = new DateTimeOffset(DateTime.SpecifyKind(device.IssuedAt, DateTimeKind.Utc));
        return context.SignInAsync(Scheme, new ClaimsPrincipal(identity), new AuthenticationProperties
        {
            IsPersistent = true,
            IssuedUtc = issued,
            ExpiresUtc = issued.AddDays(days)
        });
    }
}
#endif
