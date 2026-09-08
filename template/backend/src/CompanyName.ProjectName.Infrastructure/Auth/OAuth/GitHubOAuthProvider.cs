#if (LocalIdentity)
using Leistd.ExceptionHandling;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using CompanyName.ProjectName.Domain.Auth.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CompanyName.ProjectName.Infrastructure.Auth.OAuth;

/// <summary>
/// GitHub OAuth 服务实现
/// </summary>
public class GitHubOAuthProvider(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<GitHubOAuthProvider> logger) : IOAuthProvider
{
    private const string AuthorizationEndpoint = "https://github.com/login/oauth/authorize";
    private const string TokenEndpoint = "https://github.com/login/oauth/access_token";
    private const string UserInfoEndpoint = "https://api.github.com/user";

    public string Name => "github";

    public string GetAuthorizationUrl(string redirectUri, string state)
    {
        var clientId = configuration["ExternalAuth:Github:ClientId"]
            ?? throw new NotFoundException("Client ID for external identity provider GitHub is not configured.")
#if (IncludeLocalization)
                .WithCode("ExternalAuth:ClientIdNotConfigured")
                .WithData("Provider", "GitHub")
#endif
            ;

        return $"{AuthorizationEndpoint}?client_id={clientId}&redirect_uri={Uri.EscapeDataString(redirectUri)}&state={state}&scope=user:email";
    }

    public async Task<OAuthTokenInfo> ExchangeCodeForTokenAsync(
        string code,
        string redirectUri,
        CancellationToken cancellationToken = default)
    {
        var clientId = configuration["ExternalAuth:Github:ClientId"]
            ?? throw new NotFoundException("Client ID for external identity provider GitHub is not configured.")
#if (IncludeLocalization)
                .WithCode("ExternalAuth:ClientIdNotConfigured")
                .WithData("Provider", "GitHub")
#endif
            ;
        var clientSecret = configuration["ExternalAuth:Github:ClientSecret"]
            ?? throw new NotFoundException("Client secret for external identity provider GitHub is not configured.")
#if (IncludeLocalization)
                .WithCode("ExternalAuth:ClientSecretNotConfigured")
                .WithData("Provider", "GitHub")
#endif
            ;

        var httpClient = httpClientFactory.CreateClient();

        var requestData = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["code"] = code,
            ["redirect_uri"] = redirectUri
        };

        var response = await httpClient.PostAsync(TokenEndpoint, new FormUrlEncodedContent(requestData), cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        var queryParams = HttpUtility.ParseQueryString(responseContent);
        var accessToken = queryParams["access_token"];

        if (string.IsNullOrEmpty(accessToken))
        {
            logger.LogError("Failed to obtain GitHub access token: {Response}", responseContent);
            throw new BadRequestException("Failed to obtain GitHub access token.")
#if (IncludeLocalization)
                .WithCode("ExternalAuth:GitHubTokenFailed")
#endif
            ;
        }

        return new OAuthTokenInfo
        {
            AccessToken = accessToken,
            TokenType = queryParams["token_type"],
            Scope = queryParams["scope"]
        };
    }

    public async Task<ExternalUserInfo> GetUserInfoAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        var httpClient = httpClientFactory.CreateClient();
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
        httpClient.DefaultRequestHeaders.Add("User-Agent", "MyProject-App");

        var response = await httpClient.GetAsync(UserInfoEndpoint, cancellationToken);
        response.EnsureSuccessStatusCode();

        var userInfo = await response.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>(cancellationToken);
        if (userInfo == null)
        {
            throw new BadRequestException("Failed to obtain GitHub user information.")
#if (IncludeLocalization)
                .WithCode("ExternalAuth:GitHubUserInfoFailed")
#endif
            ;
        }

        return new ExternalUserInfo
        {
            ProviderId = userInfo["id"].GetInt64().ToString(),
            Email = userInfo.TryGetValue("email", out var email) ? email.GetString() : null,
            Username = userInfo["login"].GetString()!,
            DisplayName = userInfo.TryGetValue("name", out var name) ? name.GetString() : null,
            AvatarUrl = userInfo.TryGetValue("avatar_url", out var avatar) ? avatar.GetString() : null
        };
    }
}
#endif
