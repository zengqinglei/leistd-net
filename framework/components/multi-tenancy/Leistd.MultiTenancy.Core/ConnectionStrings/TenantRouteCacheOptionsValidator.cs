using Microsoft.Extensions.Options;

namespace Leistd.MultiTenancy.ConnectionStrings;

// 越界值启动即失败：它同时决定改路由前的排空等待，过长的 TTL 让切换流程不可操作
internal sealed class TenantRouteCacheOptionsValidator : IValidateOptions<TenantRouteCacheOptions>
{
    public ValidateOptionsResult Validate(string? name, TenantRouteCacheOptions options) =>
        options.CacheLifetime > TimeSpan.Zero && options.CacheLifetime <= TenantRouteCacheOptions.MaximumCacheLifetime
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{TenantRouteCacheOptions.SectionName}:CacheLifetime must be greater than zero and " +
                $"at most {TenantRouteCacheOptions.MaximumCacheLifetime} (was '{options.CacheLifetime}'). " +
                "It determines how long a changed tenant route may still be served by warm instances, and therefore " +
                "how long the deactivate-and-drain step must wait before the route can be changed.");
}
