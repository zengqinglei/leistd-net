#if (RemoteTokenAuth)
using System.Globalization;
using CompanyName.ProjectName.Domain.Systems.Abstractions;
using Leistd.ServiceClient.Exceptions;
using Leistd.ServiceClient.Http;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace CompanyName.ProjectName.Infrastructure.Systems;

/// <summary>目录按显示语言在本进程缓存一分钟；拒绝或故障降级为空，不影响登录会话。</summary>
public sealed class IdentitySystemDirectory(IIdentitySystemsApi api, HybridCache cache, ILogger<IdentitySystemDirectory> logger)
    : IIdentitySystemDirectory
{
    public async Task<IReadOnlyList<SystemDirectoryEntry>> GetListAsync(CancellationToken cancellationToken = default)
    {
        var culture = CultureInfo.CurrentUICulture.Name;
        try
        {
            return await cache.GetOrCreateAsync<IReadOnlyList<SystemDirectoryEntry>>(
                "identity-system-directory:" + culture, async token =>
                {
                    using var response = await api.GetListAsync(culture, token);
                    return (await response.ReadContentAsync<List<SystemDirectoryEntry>>(cancellationToken: token))!;
                }, new HybridCacheEntryOptions
                {
                    Expiration = TimeSpan.FromMinutes(1), LocalCacheExpiration = TimeSpan.FromMinutes(1),
                    Flags = HybridCacheEntryFlags.DisableDistributedCache
                }, cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (exception is RemoteServiceException or ServiceClientException)
        {
            logger.LogError(exception, "Reading the system directory from Identity failed; verify the system-directory.read grant");
            return [];
        }
    }
}
#endif
