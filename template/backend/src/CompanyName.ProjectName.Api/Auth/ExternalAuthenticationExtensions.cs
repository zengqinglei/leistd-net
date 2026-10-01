#if (ExternalLogin)
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
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
                options.CallbackPath = "/api/v1/external-auth/google/signin";
                options.Events.OnCreatingTicket = context =>
                {
                    var user = context.User;
                    var id = Text(user, "sub") ?? throw new InvalidOperationException("Google user-info contains no sub.");
                    context.Properties.Items[UserInfoKey] = JsonSerializer.Serialize(new ExternalUserInfo
                    {
                        ProviderId = id, ProviderAccountLabel = Text(user, "email") ?? id,
                        Email = Text(user, "email"), EmailVerified = Flag(user, "email_verified"),
                        DisplayName = Text(user, "name"), AvatarUrl = Text(user, "picture")
                    });
                    return Task.CompletedTask;
                };
            });
        if (providers.Github.IsAvailable)
            authentication.AddOAuth(AuthenticationSchemeNames.ExternalProviderPrefix + "github", options =>
            {
                options.ClientId = providers.Github.ClientId!;
                options.ClientSecret = providers.Github.ClientSecret!;
                options.SignInScheme = AuthenticationSchemeNames.ExternalCookie;
                options.Events.OnRemoteFailure = RejectRemoteFailureAsync;
                options.CallbackPath = "/api/v1/external-auth/github/signin";
                options.AuthorizationEndpoint = "https://github.com/login/oauth/authorize";
                options.TokenEndpoint = "https://github.com/login/oauth/access_token";
                options.UserInformationEndpoint = "https://api.github.com/user";
                options.UsePkce = true;
                options.Scope.Add("user:email");
                options.Events.OnCreatingTicket = CreateGitHubTicketAsync;
            });
    }

    private static Task RejectRemoteFailureAsync(RemoteFailureContext context)
    {
        // 协议失败不签发外部票据；API 的问题详情管道补齐响应体。
        context.HandleResponse();
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return Task.CompletedTask;
    }

    private static async Task CreateGitHubTicketAsync(OAuthCreatingTicketContext context)
    {
        using var userResponse = await GetGitHubAsync(context, context.Options.UserInformationEndpoint);
        userResponse.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await userResponse.Content.ReadAsStringAsync(context.HttpContext.RequestAborted));
        var user = document.RootElement;
        string? email = Text(user, "email");
        var verified = false;
        try
        {
            using var response = await GetGitHubAsync(context, "https://api.github.com/user/emails");
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
        var id = user.GetProperty("id").ToString();
        var login = Text(user, "login") ?? id;
        context.Identity!.AddClaim(new Claim(ClaimTypes.NameIdentifier, id));
        context.Properties.Items[UserInfoKey] = JsonSerializer.Serialize(new ExternalUserInfo
        {
            ProviderId = id, ProviderAccountLabel = login, SuggestedUsername = login,
            Email = email, EmailVerified = verified, DisplayName = Text(user, "name"), AvatarUrl = Text(user, "avatar_url")
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
