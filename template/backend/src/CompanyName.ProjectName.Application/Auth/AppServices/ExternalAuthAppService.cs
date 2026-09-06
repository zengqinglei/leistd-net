#if (LocalIdentity)
using Leistd.UnitOfWork.Attributes;
using Leistd.ExceptionHandling;
using CompanyName.ProjectName.Application.Auth.Dtos;
using CompanyName.ProjectName.Domain.Auth.Abstractions;
using CompanyName.ProjectName.Domain.Auth.DomainServices;
using CompanyName.ProjectName.Domain.Auth.Options;
using Leistd.Ddd.Application.AppService;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace CompanyName.ProjectName.Application.Auth.AppServices;

/// <summary>
/// 外部身份验证服务
/// </summary>
internal sealed class ExternalAuthAppService(
    ExternalAuthDomainService externalAuthDomainService,
    IEnumerable<IOAuthProvider> oauthProviders,
    SessionSignInService sessionSignInService,
    IOptions<ExternalAuthOptions> externalAuthOptions) : BaseAppService(), IExternalAuthAppService
{
    private readonly ExternalAuthOptions _externalAuthOptions = externalAuthOptions.Value;

    /// <summary>
    /// 获取外部登录 URL
    /// </summary>
    public ExternalLoginUrlOutputDto GetLoginUrl(string provider, string state)
    {
        var providerConfig = _externalAuthOptions.GetProviderConfig(provider);
        if (providerConfig is null)
        {
            throw new NotFoundException($"External identity provider {provider} is not configured.")
#if (IncludeLocalization)
                .WithCode("ExternalAuth:ProviderNotConfigured")
                .WithData("Provider", provider)
#endif
                ;
        }

        var redirectUri = providerConfig.RedirectUri;
        if (redirectUri is null)
        {
            throw new NotFoundException($"RedirectUri for external identity provider {provider} is not configured.")
#if (IncludeLocalization)
                .WithCode("ExternalAuth:RedirectUriNotConfigured")
                .WithData("Provider", provider)
#endif
                ;
        }

        var oauthProvider = GetProvider(provider);

        var loginUrl = oauthProvider.GetAuthorizationUrl(redirectUri, state);

        // state 只回传在 loginUrl 里：它由提供商原样回显到回调地址，客户端无需单独持有一份，
        // 真正的绑定在 HttpOnly 的状态 Cookie 上
        return new ExternalLoginUrlOutputDto { LoginUrl = loginUrl };
    }

    /// <summary>
    /// 处理外部登录回调
    /// </summary>
    /// <remarks>
    /// 建用户、分配默认角色、建外部登录连接三次写入必须同生共死：缺了连接行，
    /// 同一外部账号下次登录会再建一个用户。
    /// </remarks>
    [UnitOfWork]
    public async Task<ClaimsPrincipal> AuthenticateExternalUserAsync(
        string provider,
        ExternalLoginCallbackInputDto request,
        CancellationToken cancellationToken = default)
    {
        var providerConfig = _externalAuthOptions.GetProviderConfig(provider);
        if (providerConfig is null)
        {
            throw new NotFoundException($"External identity provider {provider} is not configured.")
#if (IncludeLocalization)
                .WithCode("ExternalAuth:ProviderNotConfigured")
                .WithData("Provider", provider)
#endif
                ;
        }

        var redirectUri = providerConfig.RedirectUri;
        if (redirectUri is null)
        {
            throw new NotFoundException($"RedirectUri for external identity provider {provider} is not configured.")
#if (IncludeLocalization)
                .WithCode("ExternalAuth:RedirectUriNotConfigured")
                .WithData("Provider", provider)
#endif
                ;
        }

        var oauthProvider = GetProvider(provider);
        var tokenInfo = await oauthProvider.ExchangeCodeForTokenAsync(request.Code, redirectUri, cancellationToken);
        var externalUserInfo = await oauthProvider.GetUserInfoAsync(tokenInfo.AccessToken, cancellationToken);
        var (user, roleNames) = await externalAuthDomainService.FindOrCreateUserAsync(
            oauthProvider.Name,
            externalUserInfo,
            cancellationToken);

        return await sessionSignInService.SignInAsync(user, roleNames, cancellationToken);
    }

    private IOAuthProvider GetProvider(string provider)
    {
        var oauthProvider = oauthProviders.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, provider, StringComparison.OrdinalIgnoreCase));
        if (oauthProvider is not null)
        {
            return oauthProvider;
        }

        throw new BadRequestException($"Unsupported external identity provider: {provider}")
#if (IncludeLocalization)
            .WithCode("ExternalAuth:ProviderNotSupported")
            .WithData("Provider", provider)
#endif
            ;
    }
}
#endif
