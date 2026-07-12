#if (IncludeIdentity)
using CompanyName.ProjectName.Application.Auth.Dtos;
using CompanyName.ProjectName.Domain.Auth.Abstractions;
using CompanyName.ProjectName.Domain.Auth.DomainServices;
using CompanyName.ProjectName.Domain.Auth.Options;
using CompanyName.ProjectName.Domain.Shared.Errors;
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
        var providerConfig = _externalAuthOptions.GetProviderConfig(provider)
            ?? throw new NotFoundException($"External identity provider '{provider}' is not configured.")
                .WithCode(BusinessErrorCodes.ExternalProviderNotConfigured)
                .WithLocalization($"Exception:{BusinessErrorCodes.ExternalProviderNotConfigured}", provider);

        var redirectUri = providerConfig.RedirectUri
            ?? throw new NotFoundException($"Redirect URI for external identity provider '{provider}' is not configured.")
                .WithCode(BusinessErrorCodes.ExternalProviderRedirectUriMissing)
                .WithLocalization($"Exception:{BusinessErrorCodes.ExternalProviderRedirectUriMissing}", provider);

        var state = Guid.NewGuid().ToString("N");

        var oauthProvider = serviceProvider.GetKeyedService<IOAuthProvider>(provider.ToLower())
            ?? throw new BadRequestException($"External identity provider '{provider}' is not supported.")
                .WithCode(BusinessErrorCodes.ExternalProviderUnsupported)
                .WithLocalization($"Exception:{BusinessErrorCodes.ExternalProviderUnsupported}", provider);

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
        var providerConfig = _externalAuthOptions.GetProviderConfig(provider)
            ?? throw new NotFoundException($"External identity provider '{provider}' is not configured.")
                .WithCode(BusinessErrorCodes.ExternalProviderNotConfigured)
                .WithLocalization($"Exception:{BusinessErrorCodes.ExternalProviderNotConfigured}", provider);

        var redirectUri = providerConfig.RedirectUri
            ?? throw new NotFoundException($"Redirect URI for external identity provider '{provider}' is not configured.")
                .WithCode(BusinessErrorCodes.ExternalProviderRedirectUriMissing)
                .WithLocalization($"Exception:{BusinessErrorCodes.ExternalProviderRedirectUriMissing}", provider);

        var user = await externalAuthDomainService.AuthenticateWithProviderAsync(
            provider,
            request.Code,
            redirectUri,
            cancellationToken);

        return user;
    }
}
#endif
