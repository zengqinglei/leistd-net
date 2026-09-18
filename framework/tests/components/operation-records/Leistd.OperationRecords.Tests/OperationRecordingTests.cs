using System.Security.Claims;
using Leistd.OperationRecords.Abstractions;
using Leistd.OperationRecords.Options;
using Leistd.OperationRecords.Services;
using Leistd.OperationRecords.Tests.TestDoubles;
using Leistd.Security.Claims;
using Leistd.Timing;
using Leistd.TestBase.Doubles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Leistd.OperationRecords.Tests;

/// <summary>
/// 记录器把「什么人、什么时间、做了什么、结果如何」从当前上下文补齐成一条记录。
/// </summary>
public sealed class OperationRecordingTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static (OperationRecorder Recorder, RecordingOperationRecordStore Store, FakeLogCollector Logs) Create(
        Claim[]? claims = null,
        string? displayName = "Grace Hopper",
        string? username = "grace",
        OperationRecordOptions? options = null)
    {
        var store = new RecordingOperationRecordStore();
        var collector = new FakeLogCollector();
        var recorder = new OperationRecorder(
            store,
            new FakeOperationActionDefinitionManager(),
            new FakeCurrentTenant(TenantId),
            new FakeCurrentUser(id: UserId, username: username, name: displayName, claims: claims),
            new FakeCorrelationIdProvider("0af7651916cd43dd8448eb211c80319c"),
            // 官方 FakeTimeProvider 驱动真实的 IClock 实现：断言钉的是生产代码的时间口径，
            // 而不是某个手写时钟替身自己的行为。
            new UtcClockProvider(new FakeTimeProvider(FixedNow)),
            Microsoft.Extensions.Options.Options.Create(options ?? new OperationRecordOptions()),
            new FakeLogger<OperationRecorder>(collector));

        return (recorder, store, collector);
    }

    [Fact]
    public async Task A_successful_record_captures_who_when_what_and_the_outcome()
    {
        var (recorder, store, _) = Create();

        await recorder.RecordSucceededAsync(
            "identity.user.created", OperationTarget.For("u-1", "Ada Lovelace"), "App.Users.Create");

        var written = Assert.Single(store.Written);
        Assert.Equal("identity.user.created", written.Action);
        Assert.Equal("u-1", written.TargetId);
        // 目标名与操作人名同为快照：改名或销号之后，审计要回答的是"当时是什么"。
        Assert.Equal("Ada Lovelace", written.TargetName);
        Assert.Equal("App.Users.Create", written.AuthorizationBasis);
        Assert.Equal(OperationRecordOutcome.Succeeded, written.Outcome);
        Assert.Equal(UserId.ToString(), written.ActorId);
        Assert.Equal(FixedNow.UtcDateTime, written.CreationTime);
        Assert.Equal("0af7651916cd43dd8448eb211c80319c", written.CorrelationId);
    }

    /// <summary>
    /// 租户归属由记录器显式盖章，不依赖宿主 DbContext 是否为 <c>BaseDbContext</c>。
    /// </summary>
    /// <remarks>漏配落值拦截器是静默的，得到的会是一批归属为空、谁也查不到的记录。</remarks>
    [Fact]
    public async Task The_tenant_is_stamped_by_the_recorder()
    {
        var (recorder, store, _) = Create();

        await recorder.RecordSucceededAsync("a", OperationTarget.For("t"), "b");

        Assert.Equal(TenantId, Assert.Single(store.Written).TenantId);
    }

    /// <summary>操作人名取显示名；没有显示名才退到登录名。</summary>
    /// <remarks>存的是<b>快照</b>：改名或销号之后，审计要回答的仍是"当时是谁"。</remarks>
    [Theory]
    [InlineData("Grace Hopper", "grace", "Grace Hopper")]
    [InlineData(null, "grace", "grace")]
    public async Task The_actor_name_is_a_snapshot_of_the_display_name(
        string? displayName, string username, string expected)
    {
        var (recorder, store, _) = Create(displayName: displayName, username: username);

        await recorder.RecordSucceededAsync("a", OperationTarget.For("t"), "b");

        Assert.Equal(expected, Assert.Single(store.Written).ActorName);
    }

    /// <summary>
    /// 模拟登录留两个人：主体上的是被模拟者，真实操作人另存。
    /// </summary>
    /// <remarks>只记前者等于把真正按下按钮的人从审计里抹掉，事后追责会指向一个什么都没做的租户管理员。</remarks>
    [Fact]
    public async Task An_impersonated_operation_records_the_real_actor_as_well()
    {
        var (recorder, store, _) = Create(claims:
        [
            new Claim(CustomClaimTypes.ImpersonatorUserId, "host-admin-1"),
            new Claim(CustomClaimTypes.ImpersonatorUserName, "Host Admin")
        ]);

        await recorder.RecordSucceededAsync("a", OperationTarget.For("t"), "b");

        var written = Assert.Single(store.Written);
        Assert.Equal(UserId.ToString(), written.ActorId);
        Assert.Equal("host-admin-1", written.ImpersonatorId);
        Assert.Equal("Host Admin", written.ImpersonatorName);
    }

    /// <summary>claim 名由宿主经选项注入，组件不写死。</summary>
    /// <remarks>签发主体的是宿主，它用什么字段名只有它知道；写死会让换了名字的宿主静默记不到真实操作人。</remarks>
    [Fact]
    public async Task The_impersonator_claim_types_come_from_options()
    {
        var (recorder, store, _) = Create(
            claims: [new Claim("act_sub", "host-admin-1")],
            options: new OperationRecordOptions { ImpersonatorIdClaimType = "act_sub" });

        await recorder.RecordSucceededAsync("a", OperationTarget.For("t"), "b");

        Assert.Equal("host-admin-1", Assert.Single(store.Written).ImpersonatorId);
    }

    /// <summary>
    /// 非自然人主体也要留得下标识
    /// </summary>
    /// <remarks>
    /// <c>ICurrentUser.Id</c> 只在 <c>sub</c> 能解析成 GUID 时有值，而机器主体是
    /// <c>client:&lt;client_id&gt;</c>、后台作业由宿主自定前缀。只认 Id 会让这两类操作
    /// 全部记成无主的——而它们恰恰是最需要事后追查的那批（没有人盯着的自动化写入）。
    /// </remarks>
    [Theory]
    [InlineData("client:reporting-svc")]
    [InlineData("job:nightly-cleanup")]
    public async Task A_non_human_subject_still_yields_an_actor_id(string subject)
    {
        var store = new RecordingOperationRecordStore();
        var recorder = new OperationRecorder(
            store,
            new FakeOperationActionDefinitionManager(),
            new FakeCurrentTenant(TenantId),
            // 后台作业/机器主体：sub 不是 GUID，因此 ICurrentUser.Id 为 null
            new FakeCurrentUser(id: null, name: "Nightly cleanup",
                claims: [new Claim(CustomClaimTypes.Subject, subject)]),
            new FakeCorrelationIdProvider(null),
            new UtcClockProvider(new FakeTimeProvider(FixedNow)),
            Microsoft.Extensions.Options.Options.Create(new OperationRecordOptions()),
            new FakeLogger<OperationRecorder>(new FakeLogCollector()));

        await recorder.RecordSucceededAsync("a", OperationTarget.For("t"), "b");

        var written = Assert.Single(store.Written);
        Assert.Equal(subject, written.ActorId);
        Assert.Equal("Nightly cleanup", written.ActorName);
    }

    [Fact]
    public async Task A_rejected_operation_is_recorded_as_failed()
    {
        var (recorder, store, _) = Create();

        await recorder.RecordFailedAsync(
            "identity.user.disabled", OperationTarget.For("u-1"), "App.Users.Update");

        Assert.Equal(OperationRecordOutcome.Failed, Assert.Single(store.Written).Outcome);
    }

    /// <summary>
    /// 被拒路径写不进去只记 <c>Error</c>，不抛
    /// </summary>
    /// <remarks>
    /// 被拒的请求本来就要以 403/400 结束。让审计的写入故障冒上去会把它变成 500，
    /// 于是真正的拒绝原因被一个基础设施错误盖掉——调用方看到的是"服务器炸了"而不是"你没有权限"。
    /// </remarks>
    [Fact]
    public async Task A_failed_write_on_the_rejected_path_is_logged_not_thrown()
    {
        var store = new ThrowingOperationRecordStore(new InvalidOperationException("db is down"));
        var collector = new FakeLogCollector();
        var recorder = new OperationRecorder(
            store,
            new FakeOperationActionDefinitionManager(),
            new FakeCurrentTenant(TenantId),
            new FakeCurrentUser(id: UserId),
            new FakeCorrelationIdProvider(null),
            new UtcClockProvider(new FakeTimeProvider(FixedNow)),
            Microsoft.Extensions.Options.Options.Create(new OperationRecordOptions()),
            new FakeLogger<OperationRecorder>(collector));

        await recorder.RecordFailedAsync("a", OperationTarget.For("t"), "b");

        var record = Assert.Single(collector.GetSnapshot());
        Assert.Equal(LogLevel.Error, record.Level);
    }

    /// <summary>
    /// 成功路径的写入故障照常上抛
    /// </summary>
    /// <remarks>
    /// 这条记录与它描述的那次变更同处一个事务边界：审计写不进去就该让业务一起失败。
    /// 吞掉会留下"发生了但没记"的账，而那正是审计存在的理由被掏空的样子。
    /// </remarks>
    [Fact]
    public async Task A_failed_write_on_the_success_path_propagates()
    {
        var store = new ThrowingOperationRecordStore(new InvalidOperationException("db is down"));
        var recorder = new OperationRecorder(
            store,
            new FakeOperationActionDefinitionManager(),
            new FakeCurrentTenant(TenantId),
            new FakeCurrentUser(id: UserId),
            new FakeCorrelationIdProvider(null),
            new UtcClockProvider(new FakeTimeProvider(FixedNow)),
            Microsoft.Extensions.Options.Options.Create(new OperationRecordOptions()),
            new FakeLogger<OperationRecorder>(new FakeLogCollector()));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => recorder.RecordSucceededAsync("a", OperationTarget.For("t"), "b"));
    }

    /// <summary>
    /// 两个由业务给值的字符串必填，空白即拒
    /// </summary>
    /// <remarks>
    /// 从三个减为两个：目标已由 <see cref="OperationTarget"/> 承载，
    /// 空白在值对象里被吸收成 <see cref="OperationTarget.None"/>，构造不出空白标识。
    /// 那条行为由 <c>A_blank_target_id_degrades_to_no_target</c> 单独钉住。
    /// </remarks>
    /// <remarks>
    /// 空串落库之后分不出"这次操作不需要授权依据"和"调用方漏传了"，
    /// 而审计表里一个分不清含义的列等于没有这一列。
    /// </remarks>
    [Theory]
    [InlineData("", "b")]
    [InlineData("  ", "b")]
    [InlineData("a", "")]
    [InlineData("a", "   ")]
    public async Task Blank_business_supplied_strings_are_rejected(
        string action, string authorizationBasis)
    {
        var (recorder, store, _) = Create();

        await Assert.ThrowsAsync<ArgumentException>(
            () => recorder.RecordSucceededAsync(action, OperationTarget.For("t"), authorizationBasis));
        Assert.Empty(store.Written);
    }

    /// <summary>
    /// 空白目标标识不再抛错，而是退化为"无目标"
    /// </summary>
    /// <remarks>
    /// 这是 <see cref="OperationTarget"/> 引入后的<b>行为变化</b>，不是放宽校验：
    /// 空白在值对象里被吸收成 <see cref="OperationTarget.None"/>，
    /// 空白标识在类型层面就构造不出来，校验点从记录器前移到了值对象。
    /// 本用例把这个变化钉住，免得日后有人以为校验被漏掉了又加回去。
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task A_blank_target_id_degrades_to_no_target(string? targetId)
    {
        var (recorder, store, _) = Create();

        await recorder.RecordSucceededAsync("a", OperationTarget.For(targetId), "b");

        var written = Assert.Single(store.Written);
        Assert.Equal(OperationTarget.NoTargetId, written.TargetId);
        Assert.Null(written.TargetName);
    }

    /// <summary>
    /// 失败原因按码与技术说明两路落库
    /// </summary>
    /// <remarks>
    /// 码走本地化资源在展示期渲染；技术说明不本地化且仅宿主可见。
    /// 存渲染好的句子会把语言永久锁死，写入时是哪国语言此后就是哪国语言。
    /// </remarks>
    [Fact]
    public async Task A_failure_reason_is_stored_as_a_code_and_a_detail()
    {
        var (recorder, store, _) = Create();

        await recorder.RecordFailedAsync(
            "order.deleted",
            OperationTarget.For("o-1", "A001"),
            "App.Orders.Delete",
            OperationFailure.Create("Order:AlreadyShipped", """{"No":"A001"}""", "payment gateway timed out"));

        var written = Assert.Single(store.Written);
        Assert.Equal("Order:AlreadyShipped", written.FailureCode);
        Assert.Equal("""{"No":"A001"}""", written.FailureData);
        Assert.Equal("payment gateway timed out", written.FailureDetail);
        Assert.Equal("A001", written.TargetName);
    }

    /// <summary>
    /// 被拒路径同样当场拒绝空白参数，不被那个 catch 吞掉
    /// </summary>
    /// <remarks>
    /// 该 catch 吞的是"写库没成功"这类运行期故障；参数漏传是确定性的编码错误，
    /// 吞掉它会让一个永远记不上的调用点一直静默存在。
    /// </remarks>
    [Fact]
    public async Task Blank_arguments_are_rejected_on_the_failed_path_too()
    {
        var (recorder, _, _) = Create();

        await Assert.ThrowsAsync<ArgumentException>(
            () => recorder.RecordFailedAsync("a", OperationTarget.For("t"), "  "));
    }

    /// <summary>
    /// 可见性由动作定义盖章；<b>未登记的动作码盖最严格的那一档</b>
    /// </summary>
    /// <remarks>
    /// 这是一条<b>安全属性</b>，不是默认值偏好：审计表在多租户下由租户管理员直接阅读，
    /// 未登记的码若默认可见给租户，就是默认泄露。反过来最坏只是"租户暂时看不到某些记录"，
    /// 补登记即可修复——安全默认往紧里选。
    /// </remarks>
    [Fact]
    public async Task An_unregistered_action_is_stamped_with_the_most_restrictive_visibility()
    {
        var (recorder, store, _) = Create();

        await recorder.RecordSucceededAsync("never.registered", OperationTarget.For("t"), "b");

        Assert.Equal(OperationVisibility.Host, Assert.Single(store.Written).Visibility);
    }

    /// <summary>已登记的动作码按它自己声明的可见性盖章。</summary>
    [Theory]
    [InlineData(OperationVisibility.Tenant)]
    [InlineData(OperationVisibility.Actor)]
    [InlineData(OperationVisibility.Host)]
    public async Task A_registered_action_is_stamped_with_its_declared_visibility(OperationVisibility declared)
    {
        var store = new RecordingOperationRecordStore();
        var recorder = new OperationRecorder(
            store,
            new FakeOperationActionDefinitionManager(
                new Dictionary<string, OperationVisibility>(StringComparer.Ordinal)
                {
                    ["user.created"] = declared
                }),
            new FakeCurrentTenant(TenantId),
            new FakeCurrentUser(id: UserId),
            new FakeCorrelationIdProvider(null),
            new UtcClockProvider(new FakeTimeProvider(FixedNow)),
            Microsoft.Extensions.Options.Options.Create(new OperationRecordOptions()),
            new FakeLogger<OperationRecorder>(new FakeLogCollector()));

        await recorder.RecordSucceededAsync("user.created", OperationTarget.For("u-1"), "b");

        Assert.Equal(declared, Assert.Single(store.Written).Visibility);
    }

    /// <summary>超长字段在写入前留下告警，供排查"记录为什么被截断"。</summary>
    [Fact]
    public async Task An_overlong_field_is_warned_about_before_it_is_truncated()
    {
        var (recorder, _, collector) = Create();

        await recorder.RecordSucceededAsync(
            new string('x', OperationRecordInfo.MaxActionLength + 1), OperationTarget.For("t"), "b");

        Assert.Contains(
            collector.GetSnapshot(),
            record => record.Level == LogLevel.Warning && record.Message.Contains("action", StringComparison.Ordinal));
    }
}
