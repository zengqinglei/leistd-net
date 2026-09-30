using System.Security.Claims;
using Leistd.ExceptionHandling;
using Leistd.MultiTenancy.Context;
using Leistd.OperationRecords.Definitions;
using Leistd.OperationRecords.Models;
using Leistd.OperationRecords.Recording;
using Leistd.OperationRecords.Stores;
using Leistd.OperationRecords.AspNetCore.Attributes;
using Leistd.OperationRecords.AspNetCore.Extensions;
using Leistd.OperationRecords.Tests.TestDoubles;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Leistd.Security.Users;
using Leistd.TestBase.Doubles;
using Leistd.Timing;
using Leistd.Tracing.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Leistd.OperationRecords.Tests;

/// <summary>
/// 授权通过之后的业务拒绝：与被拒路径同一套判据，端点与路由值取自原请求。
/// </summary>
/// <remarks>
/// 组件映射的端点里宿主没有代码可写，业务拒绝（并发冲突、目标不存在）由宿主紧接授权之后的中间件补记。
/// </remarks>
public sealed class FailedOperationRecordingTests
{
    private static (HttpContext Context, RecordingOperationRecordStore Store) Create(
        bool authenticated = true,
        object[]? metadata = null,
        Dictionary<string, object?>? routeValues = null,
        ClaimsPrincipal? user = null)
    {
        var store = new RecordingOperationRecordStore();
        var services = new ServiceCollection();
        services.AddSingleton<IOperationRecordStore>(store);
        services.AddScoped<RecordedFailureTracker>();
        services.AddTransient<IOperationRecorder>(
            provider => new PassThroughRecorder(store, provider.GetRequiredService<RecordedFailureTracker>()));

        var context = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            User = user ?? (authenticated
                ? new ClaimsPrincipal(new ClaimsIdentity([], "TestBearer"))
                : new ClaimsPrincipal(new ClaimsIdentity()))
        };

        if (metadata is not null)
        {
            context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(metadata), "test"));
        }

        foreach (var (key, value) in routeValues ?? new())
        {
            context.Request.RouteValues[key] = value;
        }

        return (context, store);
    }

    private static readonly OperationFailure Conflict = OperationFailure.FromCode("Permission:ConcurrencyConflict");

    // 走真实注册入口与真实记录器：上面那些用例用的是替身，替身自己调 MarkRecorded，
    // 因此把生产记录器里的登记删掉它们照样绿——那条判据在替身上是恒绿的。
    // 这里用 AddOperationRecords() 解析出真实 OperationRecorder，让"什么时候登记"由生产代码决定。
    private static (IServiceProvider Root, HttpContext Context) CreateWithRealRecorder(
        IOperationRecordStore store,
        string action)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(store);
        services.AddSingleton<ICurrentTenant>(new FakeCurrentTenant(null));
        services.AddSingleton<ICurrentUser>(new FakeCurrentUser());
        services.AddSingleton<ICorrelationIdProvider>(new FakeCorrelationIdProvider(null));
        services.AddSingleton<IClock>(new UtcClockProvider(new FakeTimeProvider()));
        services.AddSingleton<IOperationActionDefinitionManager>(new FakeOperationActionDefinitionManager());
        services.AddOperationRecords();

        var root = services.BuildServiceProvider();
        var scope = root.CreateScope();
        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity([], "TestBearer"))
        };
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new OperationRecordActionAttribute(action)),
            "test"));
        return (root, context);
    }

    /// <summary>真实记录器写出成功后登记，兜底据此跳过。</summary>
    /// <remarks>
    /// 钉的是生产记录器自己的登记时机。把 <c>OperationRecorder</c> 里的
    /// <c>recordedFailures.MarkRecorded(action)</c> 删掉，这条会红（写出两条）。
    /// </remarks>
    [Fact]
    public async Task The_real_recorder_registers_a_written_failure_so_the_fallback_skips_it()
    {
        var store = new RecordingOperationRecordStore();
        var (_, context) = CreateWithRealRecorder(store, "auth.password.changed");

        var recorder = context.RequestServices.GetRequiredService<IOperationRecorder>();
        await recorder.RecordFailedAsync(
            "auth.password.changed",
            OperationTarget.For("u-1", "someone"),
            "credentials-presented",
            OperationFailure.FromCode("Security:CurrentPasswordIncorrect"));

        await context.RecordFailedOperationAsync(
            OperationFailure.FromCode("Security:CurrentPasswordIncorrect"));

        Assert.Single(store.Written);
    }

    /// <summary>写库失败时不登记，兜底照常补记。</summary>
    /// <remarks>
    /// 记录器写库失败只记日志、不上抛，这次失败并没有留痕。若此时也登记，兜底会被抑制，
    /// 一次拒绝就一条记录都没有。把登记移到 <c>InsertAsync</c> 之前，这条会红。
    /// </remarks>
    [Fact]
    public async Task A_failure_whose_write_threw_is_not_registered_so_the_fallback_still_records_it()
    {
        // 记录器写不进去，兜底换一个能写的存储——两处用的是同一个作用域里的跟踪器
        var throwing = new ThrowingOperationRecordStore(new InvalidOperationException("store is down"));
        var (_, context) = CreateWithRealRecorder(throwing, "auth.password.changed");

        var recorder = context.RequestServices.GetRequiredService<IOperationRecorder>();
        await recorder.RecordFailedAsync(
            "auth.password.changed",
            OperationTarget.For("u-1", "someone"),
            "credentials-presented",
            OperationFailure.FromCode("Security:CurrentPasswordIncorrect"));

        var tracker = context.RequestServices.GetRequiredService<RecordedFailureTracker>();
        Assert.False(tracker.AlreadyRecorded("auth.password.changed"));
    }

    /// <summary>跟踪器按作用域隔离：另一个作用域里的同一动作照常记。</summary>
    /// <remarks>
    /// 去重的范围是一次请求，不是进程。把注册从 <c>TryAddScoped</c> 改成
    /// <c>TryAddSingleton</c>，这条会红——第二个请求的失败会被第一个请求的登记抑制掉。
    /// </remarks>
    [Fact]
    public async Task Another_scope_records_the_same_action_again()
    {
        var store = new RecordingOperationRecordStore();
        var (root, first) = CreateWithRealRecorder(store, "auth.password.changed");

        await first.RequestServices.GetRequiredService<IOperationRecorder>().RecordFailedAsync(
            "auth.password.changed",
            OperationTarget.For("u-1", "someone"),
            "credentials-presented",
            OperationFailure.FromCode("Security:CurrentPasswordIncorrect"));

        using var secondScope = root.CreateScope();
        var second = new DefaultHttpContext
        {
            RequestServices = secondScope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity([], "TestBearer"))
        };
        second.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new OperationRecordActionAttribute("auth.password.changed")),
            "test"));

        await second.RecordFailedOperationAsync(
            OperationFailure.FromCode("Security:CurrentPasswordIncorrect"));

        Assert.Equal(2, store.Written.Count);
    }

    /// <summary>应用服务已在拒绝处记过同一动作：兜底不再补第二条，先记的那条原样留下。</summary>
    /// <remarks>
    /// <para>这是去重要保住的那一半。应用服务手里有文案参数与业务目标名，兜底只有错误码与路由值；
    /// 兜底若覆盖或再记一条，结果是"一次失败两条记录"，或者带参数的原因退化成裸码。</para>
    /// <para>把兜底里的 <c>AlreadyRecorded</c> 判断去掉，这条会红（写出两条）。</para>
    /// </remarks>
    [Fact]
    public async Task A_failure_already_recorded_by_the_application_is_not_recorded_again()
    {
        var (context, store) = Create(metadata:
        [
            new AuthorizeAttribute { Policy = "App.Roles.ManagePermissions" },
            new OperationRecordActionAttribute("auth.permission-grants.replaced", "providerKey")
        ], routeValues: new() { ["providerKey"] = "r-1" });

        // 应用服务那一条：带文案参数，目标名是业务名字
        var recorder = context.RequestServices.GetRequiredService<IOperationRecorder>();
        await recorder.RecordFailedAsync(
            "auth.permission-grants.replaced",
            OperationTarget.For("Role/r-1", "系统管理员"),
            "App.Roles.ManagePermissions",
            OperationFailure.FromCode("Permission:ConcurrencyConflict", new Dictionary<string, object?>
            {
                ["Name"] = "系统管理员"
            }));

        // 异常冒泡后的兜底：只有错误码
        await context.RecordFailedOperationAsync(OperationFailure.FromCode("Permission:ConcurrencyConflict"));

        var written = Assert.Single(store.Written);
        // 留下的必须是带参数的那条，参数与业务目标名都不能丢。
        // 默认编码器把非 ASCII 转义成 \uXXXX，两者都是合法 JSON，展示端 JSON.parse 一致
        Assert.Equal(@"{""Name"":""\u7CFB\u7EDF\u7BA1\u7406\u5458""}", written.FailureData);
        Assert.Equal("系统管理员", written.TargetName);
    }

    /// <summary>去重按动作码，不是"每次请求一条"：另一个动作的失败照常记。</summary>
    /// <remarks>
    /// 同一次请求里出现多条不同动作的失败记录是正常的——改口令失败之后紧跟账号被锁定就是两条。
    /// 一刀切会把第二条吞掉。
    /// </remarks>
    [Fact]
    public async Task A_failure_recorded_for_another_action_does_not_suppress_this_one()
    {
        var (context, store) = Create(metadata:
        [
            new OperationRecordActionAttribute("auth.password.changed")
        ]);

        var recorder = context.RequestServices.GetRequiredService<IOperationRecorder>();
        await recorder.RecordFailedAsync(
            "auth.locked-out",
            OperationTarget.For("u-1", "someone"),
            "credentials-presented",
            OperationFailure.FromCode("Auth:UserTemporarilyLockedOut"));

        await context.RecordFailedOperationAsync(OperationFailure.FromCode("Security:CurrentPasswordIncorrect"));

        Assert.Equal(2, store.Written.Count);
        Assert.Contains(store.Written, record => record.Action == "auth.password.changed");
    }

    [Fact]
    public async Task An_endpoint_with_the_attribute_is_recorded_with_the_given_reason()
    {
        var (context, store) = Create(metadata:
        [
            new AuthorizeAttribute { Policy = "App.Roles.ManagePermissions" },
            new OperationRecordActionAttribute("auth.permission-grants.replaced", "providerKey") { TargetIdPrefix = "Role/" }
        ], routeValues: new() { ["providerKey"] = "r-1" });

        await context.RecordFailedOperationAsync(Conflict);

        var written = Assert.Single(store.Written);
        Assert.Equal("auth.permission-grants.replaced", written.Action);
        Assert.Equal("Role/r-1", written.TargetId);
        Assert.Equal("App.Roles.ManagePermissions", written.AuthorizationBasis);
        Assert.Equal(OperationRecordOutcome.Failed, written.Outcome);
        Assert.Equal("Permission:ConcurrencyConflict", written.FailureCode);
    }

    /// <summary>
    /// 业务拒绝按文档写法（错误码 + 消息参数）记录，基类 <c>Exception.Data</c> 不进记录
    /// </summary>
    /// <remarks>
    /// 消息参数是异常作者为展示提供的值，查询时据此渲染出带具体值的原因；
    /// <c>Exception.Data</c> 没有"可公开展示"的约定，带进来就是泄露面。
    /// </remarks>
    [Fact]
    public async Task A_business_exception_is_recorded_with_its_message_parameters_only()
    {
        var (context, store) = Create(metadata: [new OperationRecordActionAttribute("user.created")]);
        var exception = new BusinessException("User:EmailAlreadyUsed", "Email 'a@b.com' is already in use.")
            .WithData("Email", "a@b.com")
            .WithData("Attempts", 3);
        exception.Data["ConnectionString"] = "Host=db-01;Password=secret";

        await context.RecordFailedOperationAsync(OperationFailure.FromCode(exception.Code, exception.LocalizationData));

        var written = Assert.Single(store.Written);
        Assert.Equal(
            ("User:EmailAlreadyUsed", """{"Email":"a@b.com","Attempts":3}""", (string?)null),
            (written.FailureCode, written.FailureData, written.FailureDetail));
    }

    /// <summary>任一身份已认证即不算匿名，与官方 <c>DenyAnonymousAuthorizationRequirement</c> 一致。</summary>
    /// <remarks>回归点：曾只看 <c>User.Identity</c>（第一个身份），首身份未认证时整条记录被当成匿名丢掉。</remarks>
    [Fact]
    public async Task A_principal_authenticated_only_by_a_later_identity_is_recorded()
    {
        var (context, store) = Create(
            metadata: [new OperationRecordActionAttribute("orders.updated")],
            user: new ClaimsPrincipal([new ClaimsIdentity(), new ClaimsIdentity([], "TestBearer")]));

        await context.RecordFailedOperationAsync(Conflict);

        Assert.Single(store.Written);
    }

    /// <summary>
    /// 叠了多个策略时取最后声明的，不重新评估
    /// </summary>
    /// <remarks>
    /// 走到业务拒绝说明授权已全部通过。主体此刻不满足任何一个策略也照样取最后声明的——
    /// 重新评估会让这条记录随请求期状态漂移，而它要回答的只是"凭哪个策略放行进来的"。
    /// </remarks>
    [Fact]
    public async Task With_several_policies_the_last_declared_one_is_the_basis_without_reevaluation()
    {
        var (context, store) = Create(metadata:
        [
            new AuthorizeAttribute(),
            new AuthorizeAttribute { Policy = "App.Orders.Update" },
            new AuthorizeAttribute { Policy = "Security.RecentMfa" },
            new OperationRecordActionAttribute("orders.updated")
        ]);

        await context.RecordFailedOperationAsync(Conflict);

        Assert.Equal("Security.RecentMfa", Assert.Single(store.Written).AuthorizationBasis);
    }

    [Fact]
    public async Task Without_a_named_policy_the_basis_is_the_placeholder()
    {
        var (context, store) = Create(metadata:
        [
            new AuthorizeAttribute(),
            new OperationRecordActionAttribute("orders.updated")
        ]);

        await context.RecordFailedOperationAsync(Conflict);

        Assert.Equal("-", Assert.Single(store.Written).AuthorizationBasis);
    }

    [Fact]
    public async Task An_endpoint_without_the_attribute_is_not_recorded()
    {
        var (context, store) = Create(metadata: [new AuthorizeAttribute { Policy = "App.Orders.Update" }]);

        await context.RecordFailedOperationAsync(Conflict);

        Assert.Empty(store.Written);
    }

    /// <summary>匿名请求一律不记：没有操作人，记下来等于一个不需要凭据的审计写入面。</summary>
    [Fact]
    public async Task An_anonymous_request_is_never_recorded()
    {
        var (context, store) = Create(
            authenticated: false,
            metadata: [new OperationRecordActionAttribute("orders.updated")]);

        await context.RecordFailedOperationAsync(Conflict);

        Assert.Empty(store.Written);
    }

    /// <summary>失败路径没有通用的默认原因可补，空原因是编码错误，当场抛出。</summary>
    [Fact]
    public async Task An_empty_failure_is_rejected()
    {
        var (context, _) = Create(metadata: [new OperationRecordActionAttribute("orders.updated")]);

        await Assert.ThrowsAsync<ArgumentException>(() => context.RecordFailedOperationAsync(OperationFailure.None));
    }
}
