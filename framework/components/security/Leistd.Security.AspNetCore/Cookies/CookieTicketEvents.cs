using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Leistd.Security.AspNetCore.Cookies;

internal sealed class CookieTicketEvents(CookieAuthenticationEvents original, Type? originalType) : CookieAuthenticationEvents
{
    private CookieAuthenticationEvents GetOriginal(HttpContext context)
    {
        if (originalType is null) return original;
        if (context.Items.TryGetValue(this, out var cached)) return (CookieAuthenticationEvents)cached!;
        var events = (CookieAuthenticationEvents)context.RequestServices.GetRequiredService(originalType);
        context.Items[this] = events;
        return events;
    }

    public override async Task SigningIn(CookieSigningInContext context)
    {
        await GetOriginal(context.HttpContext).SigningIn(context);
        context.Properties.Items[DistributedTicketStore.ReferenceVersion] = DistributedTicketStore.NewVersion();
        context.HttpContext.Items[DistributedTicketStore.ExplicitSignInKey(context.Scheme.Name)] = true;
    }

    public override Task ValidatePrincipal(CookieValidatePrincipalContext context) => GetOriginal(context.HttpContext).ValidatePrincipal(context);
    public override Task CheckSlidingExpiration(CookieSlidingExpirationContext context) => GetOriginal(context.HttpContext).CheckSlidingExpiration(context);
    public override Task SignedIn(CookieSignedInContext context) => GetOriginal(context.HttpContext).SignedIn(context);
    public override Task SigningOut(CookieSigningOutContext context) => GetOriginal(context.HttpContext).SigningOut(context);
    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context) => GetOriginal(context.HttpContext).RedirectToLogin(context);
    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context) => GetOriginal(context.HttpContext).RedirectToAccessDenied(context);
    public override Task RedirectToLogout(RedirectContext<CookieAuthenticationOptions> context) => GetOriginal(context.HttpContext).RedirectToLogout(context);
    public override Task RedirectToReturnUrl(RedirectContext<CookieAuthenticationOptions> context) => GetOriginal(context.HttpContext).RedirectToReturnUrl(context);
}
