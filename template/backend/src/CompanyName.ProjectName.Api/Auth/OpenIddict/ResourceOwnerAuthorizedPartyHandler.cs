#if (OpenIddictServer)
using CompanyName.ProjectName.Application.Auth.OAuth;
using CompanyName.ProjectName.Application.Auth.Options;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace CompanyName.ProjectName.Api.Auth.OpenIddict;

/// <summary>单跳访问令牌交换按资源归属验证调用者，其余授权方检查交给官方处理器。</summary>
internal sealed class ResourceOwnerAuthorizedPartyHandler(IOptions<OAuthResourceOptions> options)
    : IOpenIddictServerHandler<OpenIddictServerEvents.ValidateTokenRequestContext>
{
    private readonly OpenIddictServerHandlers.Exchange.ValidateAuthorizedParty _official = new();

    public ValueTask HandleAsync(OpenIddictServerEvents.ValidateTokenRequestContext context)
    {
        if (!context.Request.IsTokenExchangeGrantType() ||
            context.SubjectTokenPrincipal?.GetTokenType() != TokenTypeIdentifiers.AccessToken)
            return _official.HandleAsync(context);

        // 不能把所有者注入原票据的 audience/presenter：那会改变令牌本身的信任含义。
        // 官方同类检查答 invalid_grant；这里是调用方无权交换这份令牌（部署配置问题），答 unauthorized_client：
        // invalid_grant 留给"本人凭据不再被接受"，调用方据此要求重新登录，配置错误不能让用户反复登录。
        if (string.IsNullOrEmpty(context.ClientId) || context.ActorTokenPrincipal is not null ||
            !OAuthScopes.OwnedBy(options.Value, context.ClientId).Any(context.SubjectTokenPrincipal.HasAudience))
            context.Reject(error: Errors.UnauthorizedClient, description: "The caller does not own a subject token resource.");
        return ValueTask.CompletedTask;
    }
}
#endif
