#if (IdentityService)
using CompanyName.ProjectName.Application.Auth.Dtos;
using CompanyName.ProjectName.Domain.Auth.Abstractions;
using CompanyName.ProjectName.Domain.Auth.DomainServices;
using CompanyName.ProjectName.Domain.Auth.Options;
using CompanyName.ProjectName.Domain.Users.Entities;
using Leistd.Ddd.Application.AppService;
using Leistd.Exception.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CompanyName.ProjectName.Application.Auth.AppServices;

/// <summary>
/// 外部身份验证服务
/// </summary>
public class ExternalAuthAppService(
    ExternalAuthDomainService externalAuthDomainService,
    IServiceProvider serviceProvider,
    IOptions<ExternalAuthOptions> externalAuthOptions) : BaseAppService(), IExternalAuthAppService
{
    private readonly ExternalAuthOptions _externalAuthOptions = externalAuthOptions.Value;

    /// <summary>
    /// 获取外部登录 URL
    /// </summary>
    public ExternalLoginUrlOutputDto GetLoginUrl(string provider)
    {
        var providerConfig = _externalAuthOptions.GetProviderConfig(provider);
        if (providerConfig is null)
        {
            throw new NotFoundException($"External identity provider {provider} is not configured.")
#if (IncludeLocalization)
                .WithLocalization("ExternalAuth:ProviderNotConfigured")
                .WithData("Provider", provider)
#endif
                ;
        }

        var redirectUri = providerConfig.RedirectUri;
        if (redirectUri is null)
        {
            throw new NotFoundException($"RedirectUri for external identity provider {provider} is not configured.")
#if (IncludeLocalization)
                .WithLocalization("ExternalAuth:RedirectUriNotConfigured")
                .WithData("Provider", provider)
#endif
                ;
        }

        var state = Guid.NewGuid().ToString("N");

        var oauthProvider = serviceProvider.GetKeyedService<IOAuthProvider>(provider.ToLower());
        if (oauthProvider is null)
        {
            throw new BadRequestException($"Unsupported external identity provider: {provider}")
#if (IncludeLocalization)
                .WithLocalization("ExternalAuth:ProviderNotSupported")
                .WithData("Provider", provider)
#endif
                ;
        }

        var loginUrl = oauthProvider.GetAuthorizationUrl(redirectUri, state);

        return new ExternalLoginUrlOutputDto
        {
            LoginUrl = loginUrl,
            State = state
        };
    }

    /// <summary>
    /// 处理外部登录回调
    /// </summary>
    public async Task<User> AuthenticateExternalUserAsync(string provider, ExternalLoginCallbackInputDto request, CancellationToken cancellationToken = default)
    {
        var providerConfig = _externalAuthOptions.GetProviderConfig(provider);
        if (providerConfig is null)
        {
            throw new NotFoundException($"External identity provider {provider} is not configured.")
#if (IncludeLocalization)
                .WithLocalization("ExternalAuth:ProviderNotConfigured")
                .WithData("Provider", provider)
#endif
                ;
        }

        var redirectUri = providerConfig.RedirectUri;
        if (redirectUri is null)
        {
            throw new NotFoundException($"RedirectUri for external identity provider {provider} is not configured.")
#if (IncludeLocalization)
                .WithLocalization("ExternalAuth:RedirectUriNotConfigured")
                .WithData("Provider", provider)
#endif
                ;
        }

        var user = await externalAuthDomainService.AuthenticateWithProviderAsync(
            provider,
            request.Code,
            redirectUri,
            cancellationToken);

        return user;
    }
}
#endif
