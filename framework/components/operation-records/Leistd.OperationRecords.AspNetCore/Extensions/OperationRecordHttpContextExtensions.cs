using Leistd.ExceptionHandling;
using Leistd.OperationRecords.Errors;
using Leistd.Security.Claims;
using Leistd.OperationRecords.Models;
using Leistd.OperationRecords.Recording;
using Leistd.OperationRecords.AspNetCore.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Leistd.OperationRecords.AspNetCore.Extensions;

/// <summary>
/// 按端点上的 <see cref="OperationRecordActionAttribute"/>，在授权阶段的拒绝或授权之后的业务拒绝上补记一条失败的操作记录。
/// </summary>
/// <remarks>
/// <para><b>框架只给零件，不接管宿主的处置入口。</b>被拒路径在宿主的 <c>IAuthorizationMiddlewareResultHandler</c>
/// 里调用：宿主只能注册一个结果处理器，而那里通常还承载着应用专有的处置（把账号失效改判 401、
/// 决定错误体形状），框架若占住它，宿主唯一的授权处置入口就没了。业务拒绝在宿主自己的中间件里调用：
/// 记哪些异常是宿主的策略。两处都是一行调用，"我在这里记了一笔"看得见。</para>
/// <para>这两个零件主要服务于<b>组件映射的端点</b>：宿主自己的应用服务可以在拒绝处直接调用
/// <see cref="IOperationRecorder.RecordFailedAsync"/>，组件的端点里宿主没有代码可写。</para>
/// </remarks>
public static class OperationRecordHttpContextExtensions
{
    // 端点上找不到策略名时的兜底。它不是业务词汇——业务定义的是"凭什么放行"，
    // 而这里表达的是"框架在授权阶段没能问出依据"，所以留作私有常量，不进公开 API。
    private const string UnknownAuthorizationBasis = "-";

    // 没有目标标识时的占位。与上面同值但**不是同一个概念**：一个回答"凭什么"，
    // 一个回答"对谁做的"。共用一个常量，将来改其中一个会悄悄改掉另一个。
    private const string NoTargetId = "-";

    /// <summary>
    /// 若当前请求是被拒的写操作且端点声明了动作码，记录一条失败记录；否则什么都不做。
    /// </summary>
    /// <remarks>
    /// <para><b>注解就是唯一的选择权。</b>端点上打了 <see cref="OperationRecordActionAttribute"/>
    /// 就记，没打就不记——框架不再按 HTTP 方法之类的启发式二次否决。开发者把注解放上去，
    /// 已经明确表达了"这个动作值得留痕"；框架再静默筛一道，会让打在 <c>GET</c> 导出端点上的注解
    /// 一声不响地失效。要不要记，全由调用方通过注解决定。</para>
    /// <para><b>唯一的例外是匿名请求，一律不记。</b>这不是偏好而是安全属性：匿名请求没有操作人，
    /// 记下来等于把审计表变成一个不需要凭据的写入面，任何人都能往里灌数据。任一身份已认证即不算匿名，
    /// 与官方 <c>DenyAnonymousAuthorizationRequirement</c> 一致。</para>
    /// <para><b>授权依据取实际未通过的那个策略。</b>端点只有一个具名策略时就是它；叠了多个时
    /// （如权限策略之外再要求近期 MFA），逐个重新评估，取最具体（最后声明）的未通过者——
    /// 只在被拒路径上发生。都评估通过（策略依赖请求期状态而前后不一致）时记为 <c>-</c>，
    /// 不猜一个可能是错的名字。</para>
    /// <para>调用方应在确认授权结果为 Forbidden 之后调用本方法。</para>
    /// </remarks>
    /// <param name="context">当前请求上下文。</param>
    public static Task RecordDeniedOperationAsync(this HttpContext context)
        => context.RecordDeniedOperationAsync(OperationFailure.None);

    /// <inheritdoc cref="RecordDeniedOperationAsync(HttpContext)"/>
    /// <param name="context">当前请求上下文。</param>
    /// <param name="failure">
    /// 失败原因。<see cref="OperationFailure.None"/> 时记通用的被拒码；
    /// 只给了 <c>Detail</c>（<c>OperationFailure.FromDetail</c>）也算调用方给过原因，原样保留。
    /// </param>
    public static async Task RecordDeniedOperationAsync(this HttpContext context, OperationFailure failure)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.User.HasAuthenticatedIdentity())
        {
            return;
        }

        var endpoint = context.GetEndpoint();
        var declared = endpoint?.Metadata.GetMetadata<OperationRecordActionAttribute>();
        if (endpoint is null || declared is null)
        {
            return;
        }

        var authorizationBasis = await ResolveFailedPolicyAsync(context, endpoint) ?? UnknownAuthorizationBasis;

        var recorder = context.RequestServices.GetRequiredService<IOperationRecorder>();
        // 目标只带标识、不带名字：此刻调用方正因为**无权访问该目标**而被拒。
        // 框架若为了凑一句好看的话去查名字回填，等于把他无权查看的名字写进了他能读到的记录里。
        // 这是安全属性，不是将就——后来者请不要把它当缺陷"修复"。
        // 默认带上通用的被拒码：没有原因码的失败记录事后无法按原因聚合。
        // 宿主要附业务参数时自己传 OperationFailure.FromCode(code, data)
        await recorder.RecordFailedAsync(
            declared.Action,
            OperationTarget.For(ResolveTargetId(context.Request.RouteValues, declared)),
            authorizationBasis,
            // 判据是 IsEmpty 而不是 Code is null：FromDetail 给出的原因本来就没有码，
            // 按 Code 判会把调用方显式传入的 Detail 静默换成通用 Forbidden
            failure.IsEmpty
                ? OperationFailure.FromCode(OperationFailureCodes.Forbidden)
                : failure);
    }

    /// <summary>
    /// 若当前请求的端点声明了动作码，在授权通过之后的业务拒绝上记录一条失败记录；否则什么都不做。
    /// </summary>
    /// <remarks>
    /// <para><b>在宿主紧接 <c>UseAuthorization()</c> 的中间件里调用</b>：捕获下游抛出的业务异常，记录后原样重抛，
    /// 响应仍交给外层的异常处理器。这个位置同时满足两个前提——授权没通过的请求到不了这里，
    /// "授权已通过"才成立；请求还在租户作用域里，记录才写进操作发生的那一层。</para>
    /// <para><b>不要在 <c>IExceptionHandler</c> 里调用。</b>异常处理器在管道最外层执行，此时租户中间件的作用域
    /// 已经随异常退出，租户内的失败会被写进宿主层；而且授权之前的中间件抛出的业务异常也会流到那里，
    /// 分不清是不是授权之后的拒绝。</para>
    /// <para><b>判据与被拒路径相同</b>：端点打了 <see cref="OperationRecordActionAttribute"/> 才记，
    /// 匿名请求一律不记，目标标识按 <see cref="OperationRecordActionAttribute.TargetRouteKeys"/> 解析。</para>
    /// <para><b>授权依据取端点最后声明的具名策略，不重新评估。</b>走到这里所有策略都已通过，
    /// 最后声明的是最具体的一层（端点级晚于路由组级与类级）；同一级叠加多个具名策略时记后声明者。
    /// 这与被拒路径不同，那里要重新评估、取实际未通过的那个。没有具名策略时记 <c>-</c>。</para>
    /// <para><b>带注解端点的业务拒绝由这里统一记</b>，应用服务不要在同一次拒绝上再调
    /// <see cref="IOperationRecorder.RecordFailedAsync"/>，否则一次失败两条记录。</para>
    /// </remarks>
    /// <param name="context">当前请求上下文。</param>
    /// <param name="failure">
    /// 失败原因，不能为空：失败路径没有通用的默认原因可补。拒绝来自 <see cref="BusinessException"/> 时，
    /// 调用方显式传 <c>OperationFailure.FromCode(exception.Code, exception.LocalizationData)</c>：
    /// 消息参数本就随错误响应展示，查询时据此渲染出带具体值的原因；基类 <c>Exception.Data</c> 与异常文本不要带进来。
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="failure"/> 为空。</exception>
    public static async Task RecordFailedOperationAsync(this HttpContext context, OperationFailure failure)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (failure.IsEmpty)
        {
            throw new ArgumentException("A failed operation needs a reason: pass a code or a detail.", nameof(failure));
        }

        if (!context.User.HasAuthenticatedIdentity())
        {
            return;
        }

        var endpoint = context.GetEndpoint();
        var declared = endpoint?.Metadata.GetMetadata<OperationRecordActionAttribute>();
        if (endpoint is null || declared is null)
        {
            return;
        }

        var recorder = context.RequestServices.GetRequiredService<IOperationRecorder>();
        await recorder.RecordFailedAsync(
            declared.Action,
            OperationTarget.For(ResolveTargetId(context.Request.RouteValues, declared)),
            ResolveDeclaredPolicy(endpoint) ?? UnknownAuthorizationBasis,
            failure);
    }

    // 最后声明的具名策略。类级 [Authorize] 用默认策略、Policy 为空，不参与。
    private static string? ResolveDeclaredPolicy(Endpoint endpoint)
        => endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Select(data => data.Policy)
            .LastOrDefault(name => !string.IsNullOrEmpty(name));

    // 实际未通过的具名策略。按书写顺序取最后一个会在叠加策略时记错：权限策略之后再叠
    // 一个 MFA 策略，被 MFA 拒绝时记下的却是权限名，而且只能靠特性书写顺序规避。
    // 类级 [Authorize] 用默认策略、Policy 为空，不参与。
    private static async Task<string?> ResolveFailedPolicyAsync(HttpContext context, Endpoint endpoint)
    {
        var policyNames = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Select(data => data.Policy)
            .OfType<string>()
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (policyNames.Count <= 1)
        {
            return policyNames.SingleOrDefault();
        }

        var policyProvider = context.RequestServices.GetRequiredService<IAuthorizationPolicyProvider>();
        var authorizationService = context.RequestServices.GetRequiredService<IAuthorizationService>();

        // 从最具体（最后声明，通常是动作级）的往前找；资源与授权中间件一致，传 HttpContext。
        for (var index = policyNames.Count - 1; index >= 0; index--)
        {
            var policy = await policyProvider.GetPolicyAsync(policyNames[index]);
            if (policy is not null
                && !(await authorizationService.AuthorizeAsync(context.User, context, policy)).Succeeded)
            {
                return policyNames[index];
            }
        }

        return null;
    }

    // 目标标识：按声明顺序取路由值并以 '/' 连接。
    // 任何一段缺失就整体记为 '-'，不记半截：半截的标识既检索不到成功路径写下的那一条，
    // 又看起来像一个真实存在的目标。
    private static string ResolveTargetId(RouteValueDictionary routeValues, OperationRecordActionAttribute declared)
    {
        if (declared.TargetRouteKeys.Count == 0)
        {
            return NoTargetId;
        }

        var segments = new List<string>(declared.TargetRouteKeys.Count);
        foreach (var key in declared.TargetRouteKeys)
        {
            if (!routeValues.TryGetValue(key, out var value)
                || value?.ToString() is not { Length: > 0 } routeValue)
            {
                return NoTargetId;
            }

            segments.Add(routeValue);
        }

        return declared.TargetIdPrefix + string.Join('/', segments);
    }
}
