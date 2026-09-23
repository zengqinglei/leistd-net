using System.Security.Claims;
using System.Text;
using Leistd.ExceptionHandling;
using Leistd.OperationRecords.Definitions;
using Leistd.OperationRecords.Models;
using Leistd.OperationRecords.Queries;
using Leistd.OperationRecords.Recording;
using Leistd.OperationRecords.Stores;
using Leistd.OperationRecords.Dtos;
using Leistd.OperationRecords.Options;
using Leistd.OperationRecords.Tests.TestDoubles;
using Leistd.Security.Claims;
using Leistd.TestBase.Doubles;
using Leistd.Timing;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Leistd.OperationRecords.Tests;

/// <summary>
/// 查询用例：读者判定、筛选展开、字段裁剪与导出，分页与导出共用同一份判定。
/// </summary>
/// <remarks>
/// 读者判定写错的症状是越权而不是报错：租户读者看到宿主层记录、仅宿主字段下发给租户、
/// "界面看不到的记录能被导出来"。这些都只在建了租户之后才暴露。
/// </remarks>
public sealed class OperationRecordQueryTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");


    private static readonly Dictionary<string, OperationVisibility> Definitions = new(StringComparer.Ordinal)
    {
        ["user.created"] = OperationVisibility.Tenant,
        ["tenant.created"] = OperationVisibility.Host,
        ["auth.login.succeeded"] = OperationVisibility.Tenant,
        ["operation-records.exported"] = OperationVisibility.Tenant
    };

    private static (OperationRecordQueryService Service, RecordingOperationRecordStore Store) Create(
        bool hostReader,
        string? subject = "reader-1")
    {
        var store = new RecordingOperationRecordStore();
        var definitions = new CategorizedDefinitions();
        var clock = new UtcClockProvider(new FakeTimeProvider(new DateTimeOffset(2026, 9, 20, 10, 0, 0, TimeSpan.Zero)));
        var currentTenant = new FakeCurrentTenant(hostReader ? null : TenantId);
        var currentUser = new FakeCurrentUser(
            id: null,
            claims: subject is null ? [] : [new Claim(CustomClaimTypes.Subject, subject)]);
        var options = Microsoft.Extensions.Options.Options.Create(new OperationRecordOptions());
        var recorder = new OperationRecorder(
            store, definitions, currentTenant, currentUser, new FakeCorrelationIdProvider(null), clock, options,
            new FakeLogger<OperationRecorder>(new FakeLogCollector()));

        return (new OperationRecordQueryService(store, definitions, recorder, currentTenant, currentUser, clock, options), store);
    }

    private static OperationRecordInfo Record(
        string action = "user.created",
        string? actorId = "someone",
        string? targetName = "Ada",
        OperationRecordOutcome outcome = OperationRecordOutcome.Succeeded) => new()
        {
            Action = action,
            TargetId = "t-1",
            TargetName = targetName,
            AuthorizationBasis = "App.Users.Create",
            Outcome = outcome,
            Visibility = OperationVisibility.Tenant,
            ActorId = actorId,
            FailureDetail = "db timeout",
            CorrelationId = "trace-1",
            ActorTenantId = TenantId
        };

    [Fact]
    public async Task A_host_reader_queries_every_layer_and_sees_host_only_fields()
    {
        var (service, store) = Create(hostReader: true);
        store.Written.Add(Record());

        var page = await service.GetPagedListAsync(new GetOperationRecordPagedInputDto());

        Assert.Same(OperationRecordVisibilityScope.Host, store.LastQuery!.Value.Filter.Scope);
        var row = Assert.Single(page.Items);
        Assert.Equal(("db timeout", "trace-1", TenantId), (row.FailureDetail, row.CorrelationId, row.ActorTenantId));
    }

    /// <summary>
    /// 租户读者：范围按本人收窄，仅宿主字段裁掉
    /// </summary>
    /// <remarks>读者标识与记录器取操作人同一口径（claim 原始值），机器主体因此也读得到自己的 Actor 层记录。</remarks>
    [Fact]
    public async Task A_tenant_reader_is_scoped_to_itself_and_host_only_fields_are_trimmed()
    {
        var (service, store) = Create(hostReader: false, subject: "client:reporting");
        store.Written.Add(Record());

        var page = await service.GetPagedListAsync(new GetOperationRecordPagedInputDto());

        var scope = store.LastQuery!.Value.Filter.Scope;
        Assert.Equal((true, false, "client:reporting"), (scope.IsRestricted, scope.IncludesHostRecords, scope.ActorId));
        var row = Assert.Single(page.Items);
        Assert.Equal((null, null, null), (row.FailureDetail, row.CorrelationId, (Guid?)row.ActorTenantId));
    }

    /// <summary>类别与动作两个维度取交集；租户读者直接传宿主动作码也会被滤掉。</summary>
    [Fact]
    public async Task Categories_and_actions_intersect_within_what_the_reader_can_see()
    {
        var (service, store) = Create(hostReader: false);

        await service.GetPagedListAsync(new GetOperationRecordPagedInputDto
        {
            Categories = ["account", "tenant"],
            Actions = ["user.created", "tenant.created", "auth.login.succeeded"]
        });

        Assert.Equal(["user.created"], store.LastQuery!.Value.Filter.Actions);
    }

    /// <summary>
    /// 筛了但展开为空时返回空页，不下传
    /// </summary>
    /// <remarks>存储把空集合当"不过滤"；下传就把"筛了但没有"显示成了"这就是全部"。</remarks>
    [Fact]
    public async Task A_filter_that_expands_to_nothing_returns_an_empty_page_without_querying()
    {
        var (service, store) = Create(hostReader: false);
        store.Written.Add(Record());

        var page = await service.GetPagedListAsync(new GetOperationRecordPagedInputDto { Actions = ["tenant.created"] });

        Assert.Empty(page.Items);
        Assert.Null(store.LastQuery);
    }

    [Fact]
    public async Task Filter_options_hide_host_actions_from_tenant_readers()
    {
        var (tenantService, _) = Create(hostReader: false);
        var (hostService, _) = Create(hostReader: true);

        var tenantOptions = await tenantService.GetFilterOptionsAsync();
        var hostOptions = await hostService.GetFilterOptionsAsync();

        Assert.DoesNotContain(tenantOptions.Actions, a => a.Code == "tenant.created");
        Assert.DoesNotContain("tenant", tenantOptions.Categories);
        Assert.Contains(hostOptions.Actions, a => a.Code == "tenant.created");
    }

    /// <summary>自证类动作成功、记录里没有操作人时，目标承载"什么人"；失败的或有操作人的不算。</summary>
    [Theory]
    [InlineData("auth.login.succeeded", null, OperationRecordOutcome.Succeeded, true)]
    [InlineData("auth.login.succeeded", null, OperationRecordOutcome.Failed, false)]
    [InlineData("auth.login.succeeded", "someone", OperationRecordOutcome.Succeeded, false)]
    [InlineData("user.created", null, OperationRecordOutcome.Succeeded, false)]
    public async Task The_target_stands_in_for_the_actor_only_for_verified_self_acting_success(
        string action, string? actorId, OperationRecordOutcome outcome, bool expected)
    {
        var (service, store) = Create(hostReader: true);
        store.Written.Add(Record(action, actorId, outcome: outcome));

        var page = await service.GetPagedListAsync(new GetOperationRecordPagedInputDto());

        Assert.Equal(expected, Assert.Single(page.Items).ActorIsTarget);
    }

    [Fact]
    public async Task An_inverted_time_range_is_rejected_with_field_errors()
    {
        var (service, _) = Create(hostReader: true);

        var error = await Assert.ThrowsAsync<System.ComponentModel.DataAnnotations.ValidationException>(() => service.GetPagedListAsync(
            new GetOperationRecordPagedInputDto { StartTime = DateTime.UtcNow, EndTime = DateTime.UtcNow.AddDays(-1) }));

        Assert.Equal(["StartTime", "EndTime"], error.ValidationResult?.MemberNames);
    }

    /// <summary>
    /// 导出：带 BOM、用户可控字段防公式注入、仅宿主列只在宿主导出时成列，文件生成后才记导出
    /// </summary>
    /// <remarks>
    /// 目标名是用户可控内容：显示名改成 <c>=cmd|...</c> 的人能让打开审计文件的管理员执行命令。
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_export_is_safe_to_open_and_audited(bool hostReader)
    {
        var (service, store) = Create(hostReader);
        store.Written.Add(Record(targetName: "=cmd|' /C calc'!A0"));

        var file = await service.ExportAsync(
            new ExportOperationRecordsInputDto(),
            new OperationRecordExportAudit("operation-records.exported", "App.OperationRecords.Export"));

        var bom = Encoding.UTF8.GetPreamble();
        Assert.Equal(bom, file.Content.Take(bom.Length));
        var text = Encoding.UTF8.GetString(file.Content, bom.Length, file.Content.Length - bom.Length);
        Assert.Contains("'=cmd", text);
        Assert.Equal(hostReader, text.Contains("CorrelationId", StringComparison.Ordinal));
        Assert.Equal(("text/csv", "operation-records-20260920100000.csv"), (file.ContentType, file.FileName));

        var audit = store.Written[^1];
        Assert.Equal(("operation-records.exported", "App.OperationRecords.Export"), (audit.Action, audit.AuthorizationBasis));
    }

    // 类别按动作码前缀划分，只为让用例能表达"按类别展开"
    private sealed class CategorizedDefinitions : IOperationActionDefinitionManager
    {
        private readonly List<IOperationActionDefinition> _all =
        [
            .. Definitions.Select(pair => (IOperationActionDefinition)new FakeOperationActionDefinition(
                pair.Key,
                pair.Value,
                category: pair.Key.StartsWith("user.", StringComparison.Ordinal) ? "account"
                    : pair.Key.StartsWith("tenant.", StringComparison.Ordinal) ? "tenant"
                    : pair.Key.StartsWith("auth.", StringComparison.Ordinal) ? "authentication" : "data",
                targetIsActor: pair.Key == "auth.login.succeeded"))
        ];

        public IOperationActionDefinition? GetOrNull(string code) => _all.FirstOrDefault(d => d.Code == code);
        public IReadOnlyList<IOperationActionDefinition> GetAll() => _all;
        public IReadOnlyList<string> GetCategories() => [.. _all.Select(d => d.Category).Distinct()];
    }
}
