#if (OpenIddictServer)
using Leistd.BackgroundJobs.Recurring;
using OpenIddict.Abstractions;
using Leistd.Timing;

namespace CompanyName.ProjectName.Application.Auth.BackgroundJobs;

/// <summary>清理控制库中的过期令牌及无效授权；沿用官方 Quartz 默认的 14 天最小保留期。</summary>
internal sealed class OpenIddictPruningJob(IOpenIddictTokenManager tokens, IOpenIddictAuthorizationManager authorizations, IClock clock) : IRecurringJob
{
    internal const string Name = "auth.openiddict.prune";
    internal static readonly TimeSpan MinimumRetention = TimeSpan.FromDays(14);

    public async Task ExecuteAsync(RecurringJobContext context, CancellationToken cancellationToken)
    {
        var threshold = new DateTimeOffset(DateTime.SpecifyKind(clock.Now, DateTimeKind.Utc)) - MinimumRetention;
        // 先删令牌，再删已没有有效令牌引用的授权；官方管理器负责状态与引用判据。
        await tokens.PruneAsync(threshold, cancellationToken);
        await authorizations.PruneAsync(threshold, cancellationToken);
    }
}
#endif
