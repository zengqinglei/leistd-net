using System.Security.Claims;
using Leistd.ExceptionHandling;
using Leistd.OperationRecords.Models;
using Leistd.OperationRecords.Recording;
using Leistd.OperationRecords.Stores;
using Leistd.OperationRecords.AspNetCore.Attributes;
using Leistd.OperationRecords.AspNetCore.Extensions;
using Leistd.OperationRecords.Tests.TestDoubles;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
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
        services.AddSingleton<IOperationRecorder>(_ => new PassThroughRecorder(store));

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
