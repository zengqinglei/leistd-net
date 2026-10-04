#if (ExternalLogin)
using System.Text.Json;
using CompanyName.ProjectName.Api.Auth;
using CompanyName.ProjectName.Application.Auth.AppServices;
using CompanyName.ProjectName.Application.Shared;
using CompanyName.ProjectName.Application.Auth.Dtos;
using CompanyName.ProjectName.Domain.Auth.Abstractions;
using CompanyName.ProjectName.Domain.Auth.Errors;
using Leistd.ExceptionHandling;
using Leistd.Lock.Abstractions;
using Leistd.MultiTenancy.Context;
using Leistd.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CompanyName.ProjectName.Api.Controllers;

[Route("api/v1/external-auth")]
public sealed class ExternalAuthController(
    IExternalAuthAppService externalAuthAppService,
    IAuthenticationSchemeProvider schemes,
    IOptions<ClaimTypeOptions> claimTypes,
    ICurrentTenant currentTenant,
    TimeProvider clock) : BaseController
{
    [AllowAnonymous]
    [HttpGet("{provider}/challenge")]
    public Task<IActionResult> StartAsync(string provider, string? returnUrl = null) => StartCoreAsync(provider, "login", returnUrl);

    [Authorize]
    [HttpGet("{provider}/link/challenge")]
    public Task<IActionResult> StartLinkAsync(string provider) => StartCoreAsync(provider, "link", null);

    private async Task<IActionResult> StartCoreAsync(string provider, string intent, string? returnUrl)
    {
        provider = provider.ToLowerInvariant();
        var scheme = (await ExternalSchemesAsync()).SingleOrDefault(scheme =>
            scheme.Name == AuthenticationSchemeNames.ExternalProviderPrefix + provider);
        if (scheme is null)
            throw new BusinessException(ExternalAuthErrorCodes.ProviderNotConfigured, "The external provider is not configured.")
                .WithData("Provider", provider);
        if (returnUrl is not null && !Url.IsLocalUrl(returnUrl)) throw InvalidIntent();
        if (returnUrl is not null) returnUrl = Url.Content(returnUrl);
        var properties = new AuthenticationProperties
        {
            RedirectUri = $"/auth/external-callback/{provider}?intent={intent}",
            ExpiresUtc = clock.GetUtcNow().AddMinutes(5)
        };
        properties.Items["external.returnUrl"] = returnUrl;
        properties.Items["external.provider"] = provider;
        properties.Items["external.intent"] = intent;
        properties.Items["external.initiator"] = intent == "link" ? claimTypes.Value.FindUserId(User) : null;
        properties.Items["external.tenant"] = currentTenant.Id?.ToString();
        properties.Items["external.tenant.name"] = currentTenant.Name;
        return Challenge(properties, scheme.Name);
    }

    [AllowAnonymous]
    [HttpPost("{provider}/complete")]
    public Task<IActionResult> CompleteAsync(string provider, CancellationToken cancellationToken) => CompleteCoreAsync(provider, "login", cancellationToken);

    [Authorize]
    [HttpPost("{provider}/link/complete")]
    public Task<IActionResult> CompleteLinkAsync(string provider, CancellationToken cancellationToken) => CompleteCoreAsync(provider, "link", cancellationToken);

    private async Task<IActionResult> CompleteCoreAsync(string provider, string intent, CancellationToken cancellationToken)
    {
        var ticket = await HttpContext.AuthenticateAsync(AuthenticationSchemeNames.ExternalCookie);
        if (!ticket.Succeeded || ticket.Properties is null) throw InvalidIntent();
        var items = ticket.Properties.Items;
        string? Read(string name) => items.TryGetValue(name, out var value) && !string.IsNullOrEmpty(value) ? value : null;
        if (Read("external.provider") != provider || Read("external.intent") != intent ||
            intent is not ("login" or "link") ||
            intent == "link" && (User.Identity?.IsAuthenticated != true ||
                Read("external.initiator") != claimTypes.Value.FindUserId(User) ||
                Read("external.tenant") != currentTenant.Id?.ToString())) throw InvalidIntent();
        var tenantId = Guid.TryParse(Read("external.tenant"), out var id) ? id : (Guid?)null;
#if (IncludeMultiTenancy)
        if (tenantId is { } tenant &&
            await HttpContext.RequestServices.GetRequiredService<Leistd.MultiTenancy.Stores.ITenantStore>()
                .FindAsync(tenant, cancellationToken) is not { IsActive: true }) throw InvalidIntent();
        var header = HttpContext.RequestServices.GetRequiredService<IOptions<Leistd.MultiTenancy.AspNetCore.Options.MultiTenancyOptions>>().Value.HeaderName;
        if (Request.Headers.ContainsKey(header) && tenantId != currentTenant.Id) throw InvalidIntent();
#else
        if (Read("external.tenant") is not null) throw InvalidIntent();
#endif
        var store = HttpContext.RequestServices.GetRequiredService<DistributedTicketStore>();
        var key = Read("ticket.key") ?? throw InvalidIntent();
        var locks = HttpContext.RequestServices.GetRequiredService<IDistributedLock>();
        await using var handle = await locks.TryLockAsync(key + ":complete", TimeSpan.FromSeconds(2), cancellationToken);
        if (handle is null || await store.RetrieveAsync(key) is null) throw InvalidIntent();
        // 一次消费发生在业务前：失败也不能重用这张外部票据。
        await store.RemoveAsync(key);
        await HttpContext.SignOutAsync(AuthenticationSchemeNames.ExternalCookie);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, handle.LockLost);
        using var tenantChange = currentTenant.Change(tenantId, Read("external.tenant.name"));
        var user = JsonSerializer.Deserialize<ExternalUserInfo>(items[ExternalAuthenticationExtensions.UserInfoKey]!) ?? throw InvalidIntent();
        if (intent == "link")
        {
            await externalAuthAppService.LinkCurrentUserAsync(provider, user, operation.Token);
            return Ok(new { linked = true });
        }
        var result = await externalAuthAppService.AuthenticateExternalUserAsync(provider, user, operation.Token);
        var session = await AuthController.CompleteSessionLoginAsync(HttpContext, result);
        return Ok(session with { ReturnUrl = Read("external.returnUrl") });
    }

    /// <summary>登录页据此渲染入口；与 challenge、links 读同一份已登记的提供商目录。</summary>
    [AllowAnonymous]
    [HttpGet("providers")]
    public async Task<ExternalLoginProvidersOutputDto> GetProvidersAsync() =>
        new() { Providers = await ProviderNamesAsync() };

    [Authorize]
    [HttpGet("links")]
    public async Task<ExternalLoginsOutputDto> GetLinksAsync(CancellationToken cancellationToken) =>
        await externalAuthAppService.GetCurrentUserExternalLoginsAsync(await ProviderNamesAsync(), cancellationToken);

    private async Task<IEnumerable<AuthenticationScheme>> ExternalSchemesAsync() =>
        (await schemes.GetAllSchemesAsync()).Where(scheme =>
            scheme.Name.StartsWith(AuthenticationSchemeNames.ExternalProviderPrefix, StringComparison.Ordinal) &&
            typeof(IAuthenticationRequestHandler).IsAssignableFrom(scheme.HandlerType));

    private async Task<IReadOnlyList<string>> ProviderNamesAsync() =>
        (await ExternalSchemesAsync())
            .Select(scheme => scheme.Name[AuthenticationSchemeNames.ExternalProviderPrefix.Length..])
            .Order(StringComparer.Ordinal)
            .ToList();

    [Authorize]
    [HttpDelete("links/{id:guid}")]
    public Task UnlinkAsync(Guid id, CancellationToken cancellationToken) =>
        externalAuthAppService.UnlinkCurrentUserAsync(id, cancellationToken);

    private static BusinessException InvalidIntent() => new("ExternalAuth:InvalidState", "Invalid or expired external authentication intent.");
}
#endif
