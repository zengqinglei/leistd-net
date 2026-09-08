#if (LocalIdentity)
using Leistd.ExceptionHandling;
using System.Net.Http.Json;
using System.Text.Json;
using CompanyName.ProjectName.Domain.Auth.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CompanyName.ProjectName.Infrastructure.Auth.OAuth;

/// <summary>
/// Google OAuth 服务实现
/// </summary>
public class GoogleOAuthProvider(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<GoogleOAuthProvider> logger) : IOAuthProvider
{
    private const string AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string UserInfoEndpoint = "https://www.googleapis.com/oauth2/v2/userinfo";

    public string Name => "google";

    public string GetAuthorizationUrl(string redirectUri, string state)
    {
        var clientId = configuration["ExternalAuth:Google:ClientId"]
            ?? throw new NotFoundException("Client ID for external identity provider Google is not configured.")
#if (IncludeLocalization)
                .WithCode("ExternalAuth:ClientIdNotConfigured")
                .WithData("Provider", "Google")
#endif
            ;

        return $"{AuthorizationEndpoint}?client_id={clientId}&redirect_uri={Uri.EscapeDataString(redirectUri)}&response_type=code&scope=openid%20email%20profile&state={state}";
    }

    public async Task<OAuthTokenInfo> ExchangeCodeForTokenAsync(
        string code,
        string redirectUri,
        CancellationToken cancellationToken = default)
    {
        var clientId = configuration["ExternalAuth:Google:ClientId"]
            ?? throw new NotFoundException("Client ID for external identity provider Google is not configured.")
#if (IncludeLocalization)
                .WithCode("ExternalAuth:ClientIdNotConfigured")
                .WithData("Provider", "Google")
#endif
            ;
        var clientSecret = configuration["ExternalAuth:Google:ClientSecret"]
            ?? throw new NotFoundException("Client secret for external identity provider Google is not configured.")
#if (IncludeLocalization)
                .WithCode("ExternalAuth:ClientSecretNotConfigured")
                .WithData("Provider", "Google")
#endif
            ;

        var httpClient = httpClientFactory.CreateClient();

        var requestData = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code"
        };

        var response = await httpClient.PostAsync(TokenEndpoint, new FormUrlEncodedContent(requestData), cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogError("Google token exchange failed: {StatusCode} {Content}", response.StatusCode, errorContent);
            throw new BadRequestException($"Failed to obtain access token: {response.StatusCode}")
#if (IncludeLocalization)
                .WithCode("ExternalAuth:AccessTokenExchangeFailed")
                .WithData("StatusCode", response.StatusCode)
#endif
            ;
        }

        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        var tokenResponse = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(responseContent);

        if (tokenResponse == null || !tokenResponse.TryGetValue("access_token", out var accessTokenElement))
        {
            logger.LogError("Failed to parse Google access token: {Response}", responseContent);
            throw new BadRequestException("Failed to parse the access token.")
#if (IncludeLocalization)
                .WithCode("ExternalAuth:AccessTokenParseFailed")
#endif
            ;
        }

        var accessToken = accessTokenElement.GetString();
        if (accessToken is null)
        {
            throw new BadRequestException("Access token is empty.")
#if (IncludeLocalization)
                .WithCode("ExternalAuth:AccessTokenEmpty")
#endif
            ;
        }

        return new OAuthTokenInfo
        {
            AccessToken = accessToken,
            TokenType = tokenResponse.TryGetValue("token_type", out var tokenType) ? tokenType.GetString() : null,
            ExpiresIn = tokenResponse.TryGetValue("expires_in", out var expiresIn) ? expiresIn.GetInt32() : null,
            RefreshToken = tokenResponse.TryGetValue("refresh_token", out var refreshToken) ? refreshToken.GetString() : null,
            Scope = tokenResponse.TryGetValue("scope", out var scope) ? scope.GetString() : null
        };
    }

    public async Task<ExternalUserInfo> GetUserInfoAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        var httpClient = httpClientFactory.CreateClient();
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");

        var response = await httpClient.GetAsync(UserInfoEndpoint, cancellationToken);
        response.EnsureSuccessStatusCode();

        var userInfo = await response.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>(cancellationToken);
        if (userInfo == null)
        {
            throw new BadRequestException("Failed to obtain external user information.")
#if (IncludeLocalization)
                .WithCode("ExternalAuth:UserInfoFetchFailed")
#endif
            ;
        }

        var providerId = userInfo["id"].GetString();
        if (providerId is null)
        {
            throw new BadRequestException("The external user ID is missing.")
#if (IncludeLocalization)
                .WithCode("ExternalAuth:UserIdMissing")
#endif
            ;
        }

        return new ExternalUserInfo
        {
            ProviderId = providerId,
            Email = userInfo.TryGetValue("email", out var email) ? email.GetString() : null,
            Username = (userInfo.TryGetValue("email", out var uEmail) ? uEmail.GetString()?.Split('@')[0] : null)
                       ?? providerId,
            DisplayName = userInfo.TryGetValue("name", out var name) ? name.GetString() : null,
            AvatarUrl = userInfo.TryGetValue("picture", out var picture) ? picture.GetString() : null
        };
    }
}
#endif
