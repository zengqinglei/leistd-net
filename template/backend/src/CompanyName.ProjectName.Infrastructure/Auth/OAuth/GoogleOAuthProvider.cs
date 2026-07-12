#if (IncludeIdentity)
using System.Net.Http.Json;
using System.Text.Json;
using CompanyName.ProjectName.Domain.Shared.Errors;
using CompanyName.ProjectName.Domain.Auth.Abstractions;
using Leistd.Exception.Core;
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

    public string GetAuthorizationUrl(string redirectUri, string state)
    {
        var clientId = configuration["ExternalAuth:Google:ClientId"]
            ?? throw CreateMissingConfigurationException("ExternalAuth:Google:ClientId");

        return $"{AuthorizationEndpoint}?client_id={clientId}&redirect_uri={Uri.EscapeDataString(redirectUri)}&response_type=code&scope=openid%20email%20profile&state={state}";
    }

    public async Task<OAuthTokenInfo> ExchangeCodeForTokenAsync(
        string code,
        string redirectUri,
        CancellationToken cancellationToken = default)
    {
        var clientId = configuration["ExternalAuth:Google:ClientId"]
            ?? throw CreateMissingConfigurationException("ExternalAuth:Google:ClientId");
        var clientSecret = configuration["ExternalAuth:Google:ClientSecret"]
            ?? throw CreateMissingConfigurationException("ExternalAuth:Google:ClientSecret");

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
            logger.LogError("Google Token 交换失败: {StatusCode} {Content}", response.StatusCode, errorContent);
            throw new BadRequestException($"Google access token exchange failed with status '{response.StatusCode}'.")
                .WithCode(BusinessErrorCodes.OAuthTokenExchangeFailed)
                .WithLocalization($"Exception:{BusinessErrorCodes.OAuthTokenExchangeFailed}", "Google");
        }

        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        var tokenResponse = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(responseContent);

        if (tokenResponse == null || !tokenResponse.TryGetValue("access_token", out var accessTokenElement))
        {
            logger.LogError("解析 Google Access Token 失败: {Response}", responseContent);
            throw new BadRequestException("The Google access token response is invalid.")
                .WithCode(BusinessErrorCodes.OAuthTokenInvalid)
                .WithLocalization($"Exception:{BusinessErrorCodes.OAuthTokenInvalid}", "Google");
        }

        return new OAuthTokenInfo
        {
            AccessToken = accessTokenElement.GetString() ?? throw new BadRequestException("The Google access token is missing.")
                .WithCode(BusinessErrorCodes.OAuthAccessTokenMissing)
                .WithLocalization($"Exception:{BusinessErrorCodes.OAuthAccessTokenMissing}", "Google"),
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
            throw new BadRequestException("Google user information could not be retrieved.")
                .WithCode(BusinessErrorCodes.OAuthUserInfoFailed)
                .WithLocalization($"Exception:{BusinessErrorCodes.OAuthUserInfoFailed}", "Google");

        return new ExternalUserInfo
        {
            ProviderId = userInfo["id"].GetString() ?? throw new BadRequestException("The Google user ID is missing.")
                .WithCode(BusinessErrorCodes.OAuthProviderUserIdMissing)
                .WithLocalization($"Exception:{BusinessErrorCodes.OAuthProviderUserIdMissing}", "Google"),
            Email = userInfo.TryGetValue("email", out var email) ? email.GetString() : null,
            Username = (userInfo.TryGetValue("email", out var uEmail) ? uEmail.GetString()?.Split('@')[0] : null)
                       ?? userInfo["id"].GetString()!,
            Nickname = userInfo.TryGetValue("name", out var name) ? name.GetString() : null,
            AvatarUrl = userInfo.TryGetValue("picture", out var picture) ? picture.GetString() : null
        };
    }

    private static BusinessException CreateMissingConfigurationException(string configurationKey)
    {
        return new NotFoundException($"OAuth configuration '{configurationKey}' is missing.")
            .WithCode(BusinessErrorCodes.OAuthConfigurationMissing)
            .WithLocalization($"Exception:{BusinessErrorCodes.OAuthConfigurationMissing}", configurationKey);
    }
}
#endif
