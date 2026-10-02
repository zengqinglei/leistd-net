#if (ExternalLogin)
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using AspNet.Security.OAuth.GitHub;
using CompanyName.ProjectName.Application.Shared;
using CompanyName.ProjectName.Domain.Auth.Abstractions;
using CompanyName.ProjectName.Infrastructure.Auth.OAuth.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;

namespace CompanyName.ProjectName.Api.Auth;

internal static class ExternalAuthenticationExtensions
{
    internal const string UserInfoKey = "external.user";

    public static void AddExternalAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var providers = configuration.GetSection(ExternalAuthOptions.SectionName).Get<ExternalAuthOptions>() ?? new();
        var authentication = services.AddAuthentication().AddCookie(AuthenticationSchemeNames.ExternalCookie, cookie =>
        {
            cookie.Cookie.Name = "__Host-CompanyName.ProjectName.External";
            cookie.Cookie.HttpOnly = true;
            cookie.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            cookie.Cookie.IsEssential = true;
            cookie.ExpireTimeSpan = TimeSpan.FromMinutes(5);
            cookie.SlidingExpiration = false;
        });
        services.AddOptions<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>(AuthenticationSchemeNames.ExternalCookie)
            .Configure<DistributedTicketStore, Microsoft.Extensions.Options.IOptions<CompanyName.ProjectName.Api.Options.SessionCookieOptions>>((cookie, store, session) =>
            {
                cookie.SessionStore = store;
                cookie.Cookie.SameSite = session.Value.SameSite ?? SameSiteMode.Lax;
            }).PostConfigure(DistributedTicketStore.ConfigureCookie);
        if (providers.Google.IsAvailable)
            authentication.AddGoogle(AuthenticationSchemeNames.ExternalProviderPrefix + "google", options =>
            {
                options.ClientId = providers.Google.ClientId!;
                options.ClientSecret = providers.Google.ClientSecret!;
                options.SignInScheme = AuthenticationSchemeNames.ExternalCookie;
                options.Events.OnRemoteFailure = RejectRemoteFailureAsync;
                options.Events.OnAccessDenied = ReturnAccessDeniedAsync;
                options.CallbackPath = "/api/v1/external-auth/google/signin";
                options.Events.OnCreatingTicket = context =>
                {
                    var user = context.User;
                    var id = Text(user, "sub") ?? throw new InvalidOperationException("Google user-info contains no sub.");
                    context.Properties.Items[UserInfoKey] = JsonSerializer.Serialize(new ExternalUserInfo
                    {
                        ProviderId = id, ProviderAccountLabel = Text(user, "email") ?? id,
                        Email = Text(user, "email"), EmailVerified = Flag(user, "email_verified"),
                        DisplayName = Text(user, "name"), AvatarUrl = Text(user, "picture"),
                        ProviderDisplayName = context.Scheme.DisplayName
                    });
                    return Task.CompletedTask;
                };
            });
        if (providers.Github.IsAvailable)
            authentication.AddGitHub(AuthenticationSchemeNames.ExternalProviderPrefix + "github", options =>
            {
                options.ClientId = providers.Github.ClientId!;
                options.ClientSecret = providers.Github.ClientSecret!;
                options.SignInScheme = AuthenticationSchemeNames.ExternalCookie;
                options.Events.OnRemoteFailure = RejectRemoteFailureAsync;
                options.Events.OnAccessDenied = ReturnAccessDeniedAsync;
                options.CallbackPath = "/api/v1/external-auth/github/signin";
                options.UsePkce = true;
                options.Scope.Add("user:email");
                // 包内的邮箱补取只给地址、不给 verified，且失败即中断登录；改由 CreatingTicket 自取并降级。
                options.UserEmailsEndpoint = string.Empty;
                options.Events.OnCreatingTicket = CreateGitHubTicketAsync;
            });
    }

    // 回调是浏览器整页导航：失败时回到前端回调页并给出原因码，不签发外部票据、不把问题详情 JSON 直接呈现给用户。
    private static Task RejectRemoteFailureAsync(RemoteFailureContext context)
    {
        context.HandleResponse();
        context.Response.Redirect(FailureRedirect(context.Scheme.Name, context.Properties, "failed"));
        return Task.CompletedTask;
    }

    // 用户在提供商授权页取消（error=access_denied）由官方 AccessDenied 事件接管，不再作为协议失败处理。
    private static Task ReturnAccessDeniedAsync(AccessDeniedContext context)
    {
        context.HandleResponse();
        context.Response.Redirect(FailureRedirect(context.Scheme.Name, context.Properties, "cancelled"));
        return Task.CompletedTask;
    }

    private static string FailureRedirect(string scheme, AuthenticationProperties? properties, string reason)
    {
        // state 无法解开时没有受保护的意图，按登录处理；provider 取自已登记的 scheme 名而不是请求参数。
        var intent = properties?.Items.TryGetValue("external.intent", out var value) == true && value == "link" ? "link" : "login";
        var provider = scheme[AuthenticationSchemeNames.ExternalProviderPrefix.Length..];
        return $"/auth/external-callback/{provider}?intent={intent}&error={reason}";
    }

    private static async Task CreateGitHubTicketAsync(OAuthCreatingTicketContext context)
    {
        // 资料与 NameIdentifier 已由处理器取回并映射；这里只补"主邮箱是否已验证"。
        var user = context.User;
        string? email = Text(user, "email");
        var verified = false;
        try
        {
            using var response = await GetGitHubAsync(context, GitHubAuthenticationDefaults.UserEmailsEndpoint);
            if (response.IsSuccessStatusCode)
            {
                using var emails = JsonDocument.Parse(await response.Content.ReadAsStringAsync(context.HttpContext.RequestAborted));
                var primary = emails.RootElement.EnumerateArray().FirstOrDefault(item => Flag(item, "primary"));
                if (primary.ValueKind == JsonValueKind.Object)
                {
                    email = Text(primary, "email");
                    verified = Flag(primary, "verified");
                }
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException ||
            exception is OperationCanceledException && !context.HttpContext.RequestAborted.IsCancellationRequested)
        {
            // 邮箱资料不可用时不按邮箱关联；已有外部账号仍可登录。
            context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("ExternalAuthentication").LogWarning(exception, "GitHub email verification is unavailable.");
        }
        var id = context.Identity!.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new InvalidOperationException("GitHub user profile contains no id.");
        var login = Text(user, "login") ?? id;
        context.Properties.Items[UserInfoKey] = JsonSerializer.Serialize(new ExternalUserInfo
        {
            ProviderId = id, ProviderAccountLabel = login, SuggestedUsername = login,
            Email = email, EmailVerified = verified, DisplayName = Text(user, "name"), AvatarUrl = Text(user, "avatar_url"),
            ProviderDisplayName = context.Scheme.DisplayName
        });
    }

    private static async Task<HttpResponseMessage> GetGitHubAsync(OAuthCreatingTicketContext context, string uri)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);
        request.Headers.UserAgent.ParseAdd("CompanyName.ProjectName");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        return await context.Backchannel.SendAsync(request, context.HttpContext.RequestAborted);
    }

    private static string? Text(JsonElement user, string name) => user.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static bool Flag(JsonElement user, string name) => user.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
#endif
