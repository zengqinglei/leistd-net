#if (LocalIdentity)
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompanyName.ProjectName.Application.OperationRecords.Provider;
using CompanyName.ProjectName.Application.Permissions.Provider;
using CompanyName.ProjectName.Infrastructure.Persistence;
using Leistd.Authorization.Constants;
using Leistd.EventBus.Distributed.Events;
using Leistd.EventBus.Distributed.Stores;
using Leistd.EventBus.EntityFrameworkCore.Entities;
using Leistd.UnitOfWork;
using Leistd.UnitOfWork.EntityFrameworkCore.Database;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyName.ProjectName.IntegrationTests.DistributedEvents;

/// <summary>真实 PostgreSQL 与模板组合：回环闭环、恢复留痕及两级授权。</summary>
public sealed class DistributedEventManagementTests(ProjectWebApplicationFactory factory)
    : AuthorizationTestBase(factory), IClassFixture<ProjectWebApplicationFactory>
{
    private const string Root = "/api/v1/distributed-events";

    /// <summary>D20：从真实测试端点经发件箱、回环和收件箱完成处理。</summary>
    [Fact]
    public async Task A_test_event_is_sent_and_processed_through_the_persistent_loopback_pipeline()
    {
        using var admin = await Factory.LoginAsync("admin", ProjectWebApplicationFactory.TestAdminPassword);
        using var response = await admin.Client.PostAsync($"{Root}/test", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var output = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var id = output.RootElement.GetProperty("eventId").GetGuid();
        for (var attempt = 0; attempt < 100; attempt++)
        {
            using var inbox = JsonDocument.Parse(await admin.Client.GetStringAsync($"{Root}/inbox?keyword={id}"));
            if (inbox.RootElement.GetProperty("items").EnumerateArray().Any(item => item.GetProperty("status").GetString() == "processed"))
            {
                using var outbox = JsonDocument.Parse(await admin.Client.GetStringAsync($"{Root}/outbox?keyword={id}"));
                var sent = Assert.Single(outbox.RootElement.GetProperty("items").EnumerateArray());
                Assert.Equal("sent", sent.GetProperty("status").GetString());
                Assert.Equal(DistributedEventTestEvent.Name, sent.GetProperty("eventName").GetString());
                Assert.Equal(JsonValueKind.String, sent.GetProperty("id").ValueKind);
                Assert.Equal(HttpStatusCode.OK, (await admin.Client.GetAsync("/api/health/ready")).StatusCode);
                return;
            }
            await Task.Delay(50);
        }
        Assert.Fail("The persistent test event did not finish processing.");
    }

    /// <summary>D21：按截止与错误码重投只改变匹配死信，每个提交批次留下条数与筛选摘要。</summary>
    [Fact]
    public async Task Filter_redelivery_preserves_the_event_id_and_records_the_committed_count_and_selection()
    {
        using var admin = await Factory.LoginAsync("admin", ProjectWebApplicationFactory.TestAdminPassword);
        var before = DateTime.UtcNow;
        var eventId = Guid.NewGuid();
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            using var unit = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
            var db = await scope.ServiceProvider.GetRequiredService<IDbContextProvider<MyProjectDbContext>>().GetDbContextAsync();
            db.Add(new DistributedEventOutbox
            {
                EventId = eventId, EventName = "invoice.issued", Payload = "{}", OccurredAt = before,
                CreationTime = before, LastModificationTime = before, Status = DistributedEventOutboxStatus.DeadLettered,
                AttemptCount = 7, LastErrorCode = "message-too-large", FirstFailureAt = before.AddDays(-2)
            });
            await unit.CompleteAsync();
        }
        using var response = await admin.Client.PostAsJsonAsync($"{Root}/outbox/redeliver", new
        { Filter = new { Before = before, ErrorCode = "message-too-large" } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(1, result.RootElement.GetProperty("affectedCount").GetInt64());
        using var page = JsonDocument.Parse(await admin.Client.GetStringAsync($"{Root}/outbox?keyword={eventId}"));
        var row = Assert.Single(page.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("pending", row.GetProperty("status").GetString());
        Assert.Equal(7, row.GetProperty("attemptCount").GetInt32());
#if (IncludeOperationRecords)
        using var audit = JsonDocument.Parse(await admin.Client.GetStringAsync(
            $"/api/v1/operation-records?actions={OperationRecordActions.DistributedEventRedelivered}&outcome=succeeded"));
        var entry = Assert.Single(audit.RootElement.GetProperty("items").EnumerateArray());
        Assert.StartsWith("1: ", entry.GetProperty("targetName").GetString());
        Assert.Contains("message-too-large", entry.GetProperty("targetName").GetString());
        Assert.Contains("before", entry.GetProperty("targetName").GetString());
        Assert.Equal(PermissionConstant.DistributedEvents.Manage, entry.GetProperty("authorizationBasis").GetString());
#else
        var entry = Assert.Single(Factory.Services.GetRequiredService<OperationRecordLogCapture>().Snapshot()
            .Where(entry => Equals(OperationRecordLogCapture.Field(entry, "OperationAction"), OperationRecordActions.DistributedEventRedelivered)
                && OperationRecordLogCapture.Field(entry, "OperationOutcome")?.ToString() == "Succeeded"));
        var name = OperationRecordLogCapture.Field(entry, "OperationTargetName") as string;
        Assert.StartsWith("1: ", name);
        Assert.Contains("message-too-large", name);
        Assert.Contains("before", name);
#endif
    }

    /// <summary>只有查看权限时不能读取载荷或触发恢复，拒绝尝试按 Manage 权限留痕。</summary>
    [Fact]
    public async Task Read_only_members_can_read_summaries_but_cannot_read_payloads_or_perform_management()
    {
        using var admin = await Factory.LoginAsync("admin", ProjectWebApplicationFactory.TestAdminPassword);
        var member = await CreateUserAsync(admin.Client);
        await GrantAsync(PermissionGrantProviderNames.User, member.Id, PermissionConstant.DistributedEvents.Default);
        using var reader = await Factory.LoginAsync(member.Username, TestPassword);
        foreach (var path in new[] { "status", "catalog", "databases", "outbox", "inbox", "rejections" })
            Assert.Equal(HttpStatusCode.OK, (await reader.Client.GetAsync($"{Root}/{path}")).StatusCode);
        foreach (var path in new[] { "outbox/1/payload", "inbox/1/payload", "rejections/1/payload" })
            Assert.Equal(HttpStatusCode.Forbidden, (await reader.Client.GetAsync($"{Root}/{path}")).StatusCode);
        foreach (var path in new[] { "outbox/redeliver", "inbox/replay", "rejections/redecode", "rejections/discard", "test" })
            Assert.Equal(HttpStatusCode.Forbidden, (await reader.Client.PostAsJsonAsync($"{Root}/{path}", new { Ids = new[] { "1" } })).StatusCode);
        Assert.Contains(await OperationRecordQueries.GetFailuresAsync(Factory, admin.Client, OperationRecordActions.DistributedEventRedelivered, "-"),
            record => record.AuthorizationBasis == PermissionConstant.DistributedEvents.Manage);
    }
}
#endif
