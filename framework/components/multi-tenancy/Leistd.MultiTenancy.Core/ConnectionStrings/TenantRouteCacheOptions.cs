using Microsoft.Extensions.Options;

namespace Leistd.MultiTenancy.ConnectionStrings;

/// <summary>
/// 远端解析结果的路由缓存（配置节 <c>TenantRouting</c>）。
/// </summary>
/// <remarks>
/// <para><see cref="CacheLifetime"/> 必须显式配置，缺省即启动失败，没有代码默认值。改租户数据落点的流程是：
/// 停用租户 → 等待排空 → 迁移数据 → 改路由 → 重新启用，排空要等 <c>max(Access Token 有效期, 本 TTL)</c>。
/// TTL 藏在代码默认值里，执行流程的人就无从知道该等多久；写在配置里才可见、可审、可按环境覆盖。
/// 刻意不做"开发宽松、生产严格"的分环境校验。</para>
/// <para>与 Access Token 有效期彼此独立：令牌决定已停用租户还能被访问多久，TTL 决定已改的路由还会被沿用多久。</para>
/// </remarks>
public sealed class TenantRouteCacheOptions
{
    /// <summary>配置节名 <c>TenantRouting</c>。</summary>
    public const string SectionName = "TenantRouting";

    /// <summary>TTL 上限：更长的 TTL 会把切换流程的排空等待拖到不可操作。</summary>
    public static readonly TimeSpan MaximumCacheLifetime = TimeSpan.FromHours(1);

    /// <summary>路由缓存生存期；<see langword="null"/> 表示未配置，启动期校验失败。</summary>
    public TimeSpan? CacheLifetime { get; set; }
}

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
