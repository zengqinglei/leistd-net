using CompanyName.ProjectName.Infrastructure.OperationRecords;
using CompanyName.ProjectName.Infrastructure.Persistence;
using Leistd.MultiTenancy.Abstractions;
using Leistd.OperationRecords.Abstractions;
using Leistd.OperationRecords.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
    public async Task 到期记录搬入归档表_未到期的留在原表_且租户侧不被漏掉()
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
        var moved = await archiveService.ArchiveOlderThanAsync(cutoff, batchSize: 100);

        Assert.Equal(2, moved);

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
