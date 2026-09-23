#if (LocalIdentity)
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using CompanyName.ProjectName.Domain.Auth.Abstractions;
using CompanyName.ProjectName.Infrastructure.Auth.OAuth.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CompanyName.ProjectName.Infrastructure.Auth.OAuth;

/// <summary>
/// GitHub OAuth 服务实现
/// </summary>
internal sealed class GitHubOAuthProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<ExternalAuthOptions> options,
    ILogger<GitHubOAuthProvider> logger) : IOAuthProvider
{
    private const string AuthorizationEndpoint = "https://github.com/login/oauth/authorize";
    private const string TokenEndpoint = "https://github.com/login/oauth/access_token";
    private const string UserInfoEndpoint = "https://api.github.com/user";

    public string Name => "github";

    public bool IsAvailable => options.Value.Github.IsAvailable;

    public string GetAuthorizationUrl(string state)
    {
        var provider = GetRequiredOptions();

        return $"{AuthorizationEndpoint}?client_id={provider.ClientId}&redirect_uri={Uri.EscapeDataString(provider.RedirectUri!)}&state={state}&scope=user:email";
    }

    public async Task<OAuthTokenInfo> ExchangeCodeForTokenAsync(
        string code,
        CancellationToken cancellationToken = default)
    {
        var provider = GetRequiredOptions();
        var httpClient = httpClientFactory.CreateClient();

        var requestData = new Dictionary<string, string>
        {
            ["client_id"] = provider.ClientId!,
            ["client_secret"] = provider.ClientSecret!,
            ["code"] = code,
            ["redirect_uri"] = provider.RedirectUri!
        };

        var response = await httpClient.PostAsync(TokenEndpoint, new FormUrlEncodedContent(requestData), cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        var queryParams = HttpUtility.ParseQueryString(responseContent);
        var accessToken = queryParams["access_token"];

        if (string.IsNullOrEmpty(accessToken))
        {
            logger.LogError("Failed to obtain GitHub access token: {Response}", responseContent);
            throw new InvalidOperationException("The GitHub token response did not contain an access token.");
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
            throw new InvalidOperationException("The GitHub user-info response was empty.");
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

    private ExternalAuthOptions.ProviderOptions GetRequiredOptions()
    {
        var provider = options.Value.Github;
        if (provider.IsAvailable)
            return provider;

        throw new InvalidOperationException("External identity provider GitHub is not configured.");
    }
}
#endif
