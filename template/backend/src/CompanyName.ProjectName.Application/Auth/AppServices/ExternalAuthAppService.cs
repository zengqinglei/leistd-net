#if (LocalIdentity)
using CompanyName.ProjectName.Application.Auth.SignIn;
using Leistd.UnitOfWork.Attributes;
using Leistd.ExceptionHandling;
using CompanyName.ProjectName.Application.Auth.Dtos;
using CompanyName.ProjectName.Domain.Auth.Abstractions;
using CompanyName.ProjectName.Domain.Auth.DomainServices;
using CompanyName.ProjectName.Domain.Auth.Options;
using Leistd.Ddd.Application.AppService;
using CompanyName.ProjectName.Application.OperationRecords.Provider;
using CompanyName.ProjectName.Domain.Auth.Entities;
using CompanyName.ProjectName.Domain.Users.Entities;
using Leistd.Ddd.Domain.Repositories;
using Leistd.ObjectMapping.Abstractions;
using Leistd.OperationRecords.Abstractions;
using Leistd.Security.Users;
using Microsoft.Extensions.Options;

namespace CompanyName.ProjectName.Application.Auth.AppServices;

/// <summary>
/// 外部身份验证服务
/// </summary>
internal sealed class ExternalAuthAppService(
    ExternalAuthDomainService externalAuthDomainService,
    IEnumerable<IOAuthProvider> oauthProviders,
    SessionSignInService sessionSignInService,
    IRepository<User, Guid> userRepository,
    IRepository<ExternalLoginConnection, Guid> externalLoginRepository,
    ICurrentUser currentUser,
    IOperationRecorder operationRecorder,
    IOptions<ExternalAuthOptions> externalAuthOptions,
    IObjectMapper objectMapper) : BaseAppService(), IExternalAuthAppService
{
    private readonly ExternalAuthOptions _externalAuthOptions = externalAuthOptions.Value;

    /// <summary>
    /// 获取外部登录 URL
    /// </summary>
    public ExternalLoginUrlOutputDto GetLoginUrl(string provider, string state)
    {
        var redirectUri = GetRedirectUri(provider);

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
    public async Task<SessionLoginResult> AuthenticateExternalUserAsync(
        string provider,
        ExternalLoginCallbackInputDto request,
        CancellationToken cancellationToken = default)
    {
        var redirectUri = GetRedirectUri(provider);

        var oauthProvider = GetProvider(provider);
        var tokenInfo = await oauthProvider.ExchangeCodeForTokenAsync(request.Code, redirectUri, cancellationToken);
        var externalUserInfo = await oauthProvider.GetUserInfoAsync(tokenInfo.AccessToken, cancellationToken);
        var (user, roleNames) = await externalAuthDomainService.FindOrCreateUserAsync(
            oauthProvider.Name,
            externalUserInfo,
            cancellationToken);

        return await sessionSignInService.StartAsync(user, roleNames, cancellationToken);
    }

    /// <summary>
    /// 本人的外部账号绑定：部署已配置的每个提供商，附带本人在其下的绑定
    /// </summary>
    public async Task<ExternalLoginsOutputDto> GetCurrentUserExternalLoginsAsync(CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentUserEntityAsync(cancellationToken);
        var links = (await externalLoginRepository.GetListAsync(c => c.UserId == user.Id, cancellationToken)).ToList();

        return new ExternalLoginsOutputDto
        {
            HasPassword = user.PasswordHash is not null,
            Providers = oauthProviders
                .Where(p => _externalAuthOptions.GetProviderConfig(p.Name) is { ClientId.Length: > 0 })
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .Select(p => new ExternalLoginProviderOutputDto
                {
                    Provider = p.Name,
                    Link = links
                        .Where(l => string.Equals(l.Provider, p.Name, StringComparison.OrdinalIgnoreCase))
                        .Select(l => objectMapper.Map<ExternalLoginConnection, ExternalLoginLinkOutputDto>(l))
                        .FirstOrDefault()
                })
                .ToList()
        };
    }

    /// <summary>
    /// 把外部身份绑定到当前用户（外部授权回来后调用）
    /// </summary>
    [UnitOfWork]
    public async Task LinkCurrentUserAsync(
        string provider,
        ExternalLoginCallbackInputDto request,
        CancellationToken cancellationToken = default)
    {
        var redirectUri = GetRedirectUri(provider);
        var oauthProvider = GetProvider(provider);
        var tokenInfo = await oauthProvider.ExchangeCodeForTokenAsync(request.Code, redirectUri, cancellationToken);
        var externalUserInfo = await oauthProvider.GetUserInfoAsync(tokenInfo.AccessToken, cancellationToken);

        var user = await GetCurrentUserEntityAsync(cancellationToken);
        var link = await externalAuthDomainService.LinkAsync(user, oauthProvider.Name, externalUserInfo, cancellationToken);

        await operationRecorder.RecordSucceededAsync(
            OperationRecordActions.AuthExternalLoginLinked,
            OperationTarget.For(link.Id, $"{oauthProvider.Name}: {externalUserInfo.Username}"),
            OperationRecordAuthorizations.AuthenticatedSelf,
            cancellationToken);
    }

    /// <summary>
    /// 解绑当前用户的一个外部账号
    /// </summary>
    /// <remarks>不存在或不属于本人时静默成功：解绑是幂等的，也不借此透露别人的绑定 Id 是否存在。</remarks>
    public async Task UnlinkCurrentUserAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentUserEntityAsync(cancellationToken);
        var removed = await externalAuthDomainService.UnlinkAsync(user, id, cancellationToken);
        if (removed is null)
            return;

        await operationRecorder.RecordSucceededAsync(
            OperationRecordActions.AuthExternalLoginUnlinked,
            OperationTarget.For(removed.Id, $"{removed.Provider}: {removed.ProviderUsername}"),
            OperationRecordAuthorizations.AuthenticatedSelf,
            cancellationToken);
    }

    private string GetRedirectUri(string provider)
    {
        var providerConfig = _externalAuthOptions.GetProviderConfig(provider)
            ?? throw new NotFoundException($"External identity provider {provider} is not configured.")
#if (IncludeLocalization)
                .WithCode("ExternalAuth:ProviderNotConfigured")
                .WithData("Provider", provider)
#endif
                ;

        return providerConfig.RedirectUri
            ?? throw new NotFoundException($"RedirectUri for external identity provider {provider} is not configured.")
#if (IncludeLocalization)
                .WithCode("ExternalAuth:RedirectUriNotConfigured")
                .WithData("Provider", provider)
#endif
                ;
    }

    private async Task<User> GetCurrentUserEntityAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.Id!.Value;
        return await userRepository.GetByIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException($"User {userId} not found.")
#if (IncludeLocalization)
                .WithCode("User:NotFound")
                .WithData("Id", userId)
#endif
                ;
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
