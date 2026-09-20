using Microsoft.Extensions.Options;

namespace Leistd.MultiTenancy.ConnectionStrings;

// TTL 没有代码默认值：缺配置即启动失败，见 TenantRouteCacheOptions 的说明。
internal sealed class TenantRouteCacheOptionsValidator : IValidateOptions<TenantRouteCacheOptions>
{
    public ValidateOptionsResult Validate(string? name, TenantRouteCacheOptions options) =>
        options.CacheLifetime is { } lifetime && lifetime > TimeSpan.Zero
                                              && lifetime <= TenantRouteCacheOptions.MaximumCacheLifetime
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{TenantRouteCacheOptions.SectionName}:CacheLifetime is required and must be greater than zero and " +
                $"at most {TenantRouteCacheOptions.MaximumCacheLifetime} (was '{options.CacheLifetime}'). " +
                "It determines how long a changed tenant route may still be served by warm instances, and therefore " +
                "how long the deactivate-and-drain step must wait before the route can be changed.");
}
