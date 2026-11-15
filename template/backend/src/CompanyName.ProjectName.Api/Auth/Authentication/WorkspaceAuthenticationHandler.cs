#if (TenantWorkspace)
using System.Text.Encodings.Web;
using CompanyName.ProjectName.Application.Shared;
#if (LocalIdentity)
using CompanyName.ProjectName.Application.Auth.SignIn;
#else
using CompanyName.ProjectName.Api.Auth.Sessions;
#endif
using Leistd.MultiTenancy.Stores;
using Leistd.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CompanyName.ProjectName.Api.Auth.Authentication;

/// <summary>请求工作区唯一的认证入口；失败只拒绝当前请求，不更换本人会话。</summary>
internal sealed class WorkspaceAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    IOptions<ClaimTypeOptions> claimTypes, ITenantStore tenantStore, Leistd.MultiTenancy.Stores.ITenantNormalizer tenantNormalizer,
#if (LocalIdentity)
    IAuthPrincipalFactory principalFactory
#else
    ResourceWorkspaceTicketProvider workspaceTickets
#endif
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
#if (OpenIddictServer || ResourceBrowserSession)
    private const string InnerScheme = AuthenticationSchemeNames.Smart;
#else
    private const string InnerScheme = AuthenticationSchemeNames.SessionCookie;
#endif
    private string? _errorCode;

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var home = await Context.AuthenticateAsync(InnerScheme);
        if (!home.Succeeded || home.Principal is null) return home;
        var path = Request.Path;
        // 先处理本人端点，失效工作区也不能挡住本人管理会话与认证。
        if (path.StartsWithSegments("/api/v1/auth") || path.StartsWithSegments("/api/v1/external-auth") ||
            path.StartsWithSegments("/connect")) return home;
        var workspace = Request.Headers["X-Workspace"].ToString();
        if (string.IsNullOrEmpty(workspace) && path.StartsWithSegments("/hubs"))
            workspace = Request.Query["workspace"].ToString();
        if (string.IsNullOrEmpty(workspace)) return home;
        var tenantClaim = claimTypes.Value.ReadTenant(home.Principal);
        var tenant = await tenantStore.FindByNameAsync(tenantNormalizer.NormalizeName(workspace)!, Context.RequestAborted);
        if (tenantClaim.TenantId is { } ownTenantId)
        {
            if (tenant?.Id != ownTenantId) return Deny("Workspace:Forbidden");
            return tenant.IsActive ? home : Deny("Workspace:Unavailable");
        }
        if (!tenantClaim.IsValid || tenant is not { IsActive: true }) return Deny("Workspace:Unavailable");
#if (LocalIdentity)
        var principal = await principalFactory.CreateWorkspaceAsync(home.Principal, tenant.Id, cancellationToken: Context.RequestAborted);
        return principal is null ? Deny("Workspace:Unavailable") : AuthenticateResult.Success(
            new AuthenticationTicket(principal, home.Properties, Scheme.Name));
#else
        return await workspaceTickets.GetAsync(Context, home, tenant, Context.RequestAborted)
            ?? Deny("Workspace:Unavailable");
#endif
    }

    private AuthenticateResult Deny(string code)
    {
        _errorCode = code;
        return AuthenticateResult.Fail(code);
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        _errorCode is null ? Context.ChallengeAsync(InnerScheme, properties) : HandleForbiddenAsync(properties);

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        if (_errorCode is null) return Context.ForbidAsync(InnerScheme, properties);
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Context.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = Context,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden, Title = "Forbidden",
                Extensions = { ["code"] = _errorCode }
            }
        }).AsTask();
    }
}
#endif
