using CompanyName.ProjectName.Api.Configuration;
using CompanyName.ProjectName.Api.Options;
using CompanyName.ProjectName.Application.Settings.Hosting;
using CompanyName.ProjectName.Application.Settings.Provider;
using CompanyName.ProjectName.Infrastructure.OperationRecords;
using CompanyName.ProjectName.Infrastructure.Persistence;
using Leistd.MultiTenancy.Abstractions;
using Leistd.OperationRecords.Abstractions;
using Leistd.OperationRecords.EntityFrameworkCore.Entities;
using Leistd.Settings.Abstractions;
using Leistd.Settings.Definitions;
using Leistd.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>
/// 操作记录到期归档：搬走该搬的、留下不该搬的，且<b>不漏掉任何租户</b>。
/// </summary>
/// <remarks>
/// <para>这套用例存在的理由是一个<b>错了不会报错</b>的陷阱：<c>OperationRecord</c> 实现
/// <c>IMultiTenant</c>，带着租户全局过滤器；而归档作业跑在<b>无租户上下文</b>里，此时过滤器的语义是
/// 「只放行宿主自己的行」，<b>不是</b>「放行所有租户」。归档查询若少了
/// <c>IgnoreQueryFilters()</c>，就只会搬走宿主那部分，所有租户的记录永远留着——
/// 而日志照样打印「归档成功 N 条」，监控也看不出异常。</para>
/// <para>因此这里必须<b>同时</b>造宿主侧与租户侧的数据：只造宿主侧的用例在漏掉过滤器时照样全绿。</para>
/// </remarks>
public sealed class OperationRecordArchiveTests(ProjectWebApplicationFactory factory)
    : IClassFixture<ProjectWebApplicationFactory>
{
    private const string ExpiredHostAction = "archive-test.host.expired";
    private const string FreshHostAction = "archive-test.host.fresh";
    private const string ExpiredTenantAction = "archive-test.tenant.expired";
    private const string FreshTenantAction = "archive-test.tenant.fresh";

    [Fact]
    public async Task Expired_records_are_archived_for_host_and_tenants_alike()
    {
        var tenantId = Guid.NewGuid();
        var cutoff = DateTime.UtcNow.AddDays(-365);
        var expired = cutoff.AddDays(-35);
        var fresh = DateTime.UtcNow.AddDays(-1);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MyProjectDbContext>();
        var currentTenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();

        // 宿主侧：不在任何租户上下文里写入，TenantId 落 null。
        dbContext.Set<OperationRecord>().AddRange(
            NewRecord(ExpiredHostAction, expired),
            NewRecord(FreshHostAction, fresh));
        await dbContext.SaveChangesAsync();

        // 租户侧：TenantId 由当前租户上下文盖章，不必真的建出租户实体——
        // 归档关心的只是这一行带没带 TenantId。
        using (currentTenant.Change(tenantId))
        {
            dbContext.Set<OperationRecord>().AddRange(
                NewRecord(ExpiredTenantAction, expired),
                NewRecord(FreshTenantAction, fresh));
            await dbContext.SaveChangesAsync();
        }

        dbContext.ChangeTracker.Clear();

        var archiveService = scope.ServiceProvider.GetRequiredService<IOperationRecordArchiveService>();
        var result = await archiveService.ArchiveOlderThanAsync(cutoff, batchSize: 100);

        Assert.Equal(new OperationRecordArchiveResult(Archived: 2, Databases: 1, FailedDatabases: 0), result);

        // 读原表要 IgnoreQueryFilters：这里跑在无租户上下文里，不加的话租户那两行本就看不见，
        // 断言会在"漏掉过滤器"的缺陷下依然全绿——那样这套用例就白写了。
        var remaining = await dbContext.Set<OperationRecord>()
            .IgnoreQueryFilters()
            .Where(record => record.Action.StartsWith("archive-test."))
            .Select(record => record.Action)
            .ToListAsync();

        Assert.Equal(
            [FreshHostAction, FreshTenantAction],
            remaining.Order(StringComparer.Ordinal).ToList());

        // 归档表不实现 IMultiTenant，因此没有过滤器，直接查即可。
        var archived = await dbContext.Set<OperationRecordArchive>()
            .Where(record => record.Action.StartsWith("archive-test."))
            .ToListAsync();

        Assert.Equal(
            [ExpiredHostAction, ExpiredTenantAction],
            archived.Select(record => record.Action).Order(StringComparer.Ordinal).ToList());

        // 租户归属必须随记录一起搬过去，否则归档之后再也分不清这条是谁的。
        var archivedTenantRow = Assert.Single(archived, record => record.Action == ExpiredTenantAction);
        Assert.Equal(tenantId, archivedTenantRow.TenantId);

        var archivedHostRow = Assert.Single(archived, record => record.Action == ExpiredHostAction);
        Assert.Null(archivedHostRow.TenantId);

        // 原始发生时间保持不变；归档时刻是另一件事，两者分列。
        // 不用 Assert.Equal 的容差重载：那是 xunit v3 才有的，本仓用的是 2.9.3。
        Assert.True(
            (archivedHostRow.CreationTime - expired).Duration() < TimeSpan.FromSeconds(1),
            $"归档后的发生时间应保持不变，期望 {expired:o}，实际 {archivedHostRow.CreationTime:o}");
        Assert.NotEqual(default, archivedHostRow.ArchivedTime);
    }

    /// <summary>
    /// 是否归档、保留几天可由宿主级设置覆盖配置：设置经配置源流进 <c>IOptionsMonitor</c>，清除设置回落配置（默认关闭、365 天）；
    /// 让 Options 校验不过的值整组不生效，沿用上一组合规值。
    /// </summary>
    [Fact]
    public async Task Retention_follows_host_settings_and_keeps_the_last_valid_values()
    {
        var monitor = factory.Services.GetRequiredService<IOptionsMonitor<OperationRecordRetentionOptions>>();
        await ApplyHostSettingsAsync();
        Assert.Equal((false, 365), (monitor.CurrentValue.Enabled, monitor.CurrentValue.RetentionDays));

        try
        {
            await SetHostAsync(SettingConstant.Audit.RetentionEnabled, "true");
            await SetHostAsync(SettingConstant.Audit.RetentionDays, "90");
            await ApplyHostSettingsAsync();
            Assert.Equal((true, 90), (monitor.CurrentValue.Enabled, monitor.CurrentValue.RetentionDays));

            // 绕过接口写进库的过小值：应用不报错（值已落库，保存与刷新不该因此失败），
            // 按 Options 的区间校验被拒、整组不生效——归档照旧按上一组合规值运行，不会把最近的记录搬走，
            // 也不会在有人改正之前每次取值都抛异常
            await SetHostAsync(SettingConstant.Audit.RetentionDays, "5");
            await ApplyHostSettingsAsync();
            Assert.Equal((true, 90), (monitor.CurrentValue.Enabled, monitor.CurrentValue.RetentionDays));
        }
        finally
        {
            await SetHostAsync(SettingConstant.Audit.RetentionEnabled, null);
            await SetHostAsync(SettingConstant.Audit.RetentionDays, null);
        }

        await ApplyHostSettingsAsync();
        Assert.Equal((false, 365), (monitor.CurrentValue.Enabled, monitor.CurrentValue.RetentionDays));
    }

    /// <summary>
    /// 归档表覆盖原表的每一列
    /// </summary>
    /// <remarks>
    /// 搬运是逐字段复制，原表加了列而归档表没跟上，搬完照样报成功，缺的那列到查归档时才会发现。
    /// </remarks>
    [Fact]
    public void The_archive_carries_every_record_column()
    {
        var archiveColumns = typeof(OperationRecordArchive).GetProperties().Select(property => property.Name).ToHashSet();

        Assert.Empty(typeof(OperationRecord).GetProperties()
            .Select(property => property.Name)
            .Where(name => !archiveColumns.Contains(name)));
    }

    private async Task SetHostAsync(string name, string? value)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        using var unitOfWork = await services.GetRequiredService<IUnitOfWorkManager>().BeginAsync(requiresNew: true);
        await services.GetRequiredService<ISettingManager>().SetAsync(name, value, SettingScopes.Host);
        await unitOfWork.CompleteAsync();
    }

    // 与周期刷新同样在宿主上下文里跑应用器
    private async Task ApplyHostSettingsAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        using (scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(null))
        {
            await scope.ServiceProvider.GetServices<IHostSettingApplier>()
                .OfType<HostSettingsConfigurationApplier>()
                .Single()
                .ApplyAsync();
        }
    }

    private static OperationRecord NewRecord(string action, DateTime creationTime) => new()
    {
        Id = Guid.CreateVersion7(),
        Action = action,
        TargetId = "-",
        AuthorizationBasis = "archive-test",
        Outcome = OperationRecordOutcome.Succeeded,
        CreationTime = creationTime,
        Visibility = OperationVisibility.Tenant,
    };
}
