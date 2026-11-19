#if (RemoteTokenAuth)
using System.Globalization;
using CompanyName.ProjectName.Domain.Systems.Abstractions;
using Leistd.ServiceClient.Exceptions;
using Leistd.ServiceClient.Http;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace CompanyName.ProjectName.Infrastructure.Systems;

/// <summary>目录按显示语言缓存一分钟，部署注册 Redis 时多节点共享；拒绝或故障降级为空，不影响登录会话。</summary>
public sealed class IdentitySystemDirectory(IIdentitySystemsApi api, HybridCache cache, ILogger<IdentitySystemDirectory> logger)
    : IIdentitySystemDirectory
{
    public async Task<IReadOnlyList<IdentitySystem>> GetListAsync(CancellationToken cancellationToken = default)
    {
        var culture = CultureInfo.CurrentUICulture.Name;
        try
        {
            return await cache.GetOrCreateAsync<(IIdentitySystemsApi Api, string Culture), IReadOnlyList<IdentitySystem>>(
                "identity-system-directory:" + culture, (api, culture), static async (state, token) =>
                {
                    using var response = await state.Api.GetListAsync(state.Culture, token);
                    return (await response.ReadContentAsync<List<IdentitySystem>>(cancellationToken: token))!;
                }, new HybridCacheEntryOptions
                {
                    Expiration = TimeSpan.FromMinutes(1), LocalCacheExpiration = TimeSpan.FromMinutes(1)
                }, cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (exception is RemoteServiceException or ServiceClientException)
        {
            logger.LogError(exception, "Reading the system directory from Identity failed; verify the Identity API scope grant");
            return [];
        }
    }
}
#endif
