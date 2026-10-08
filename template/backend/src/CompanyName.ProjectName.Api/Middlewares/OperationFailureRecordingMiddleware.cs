using Leistd.ExceptionHandling;
using Leistd.OperationRecords.AspNetCore.Extensions;
using Leistd.OperationRecords.Models;

namespace CompanyName.ProjectName.Api.Middlewares;

/// <summary>授权通过之后被业务规则拒绝的写操作，补一条失败的操作记录；异常原样重抛，响应仍由全局异常处理写出。</summary>
/// <remarks>
/// <para><b>这是兜底，覆盖所有带注解的端点</b>——组件映射的端点与本项目的控制器都在内。
/// 最初要解决的是组件端点的缺口：权限管理这类由组件映射的端点，业务拒绝（并发冲突、权限未定义、
/// 主体不存在）发生在组件内部，这里没有代码能在拒绝处调记录器。但范围不按端点类型划——
/// 记不记由端点上的 <c>[OperationRecordAction]</c> 决定，没打注解就什么都不做。
/// 被拒那一半由 <see cref="Auth.Authorization.ApiAuthorizationResultHandler"/> 补记，这里补另一半。</para>
/// <para><b>应用服务照常可以自己记。</b>同一动作码在本次请求里已经记过，框架侧就跳过这里的兜底
/// （见 <c>RecordFailedOperationAsync</c>），留下的是先记的那条——它带着文案参数与业务目标名。</para>
/// <para><b>必须紧接 <c>UseAuthorization()</c>。</b>授权没通过的请求到不了这里，"授权已通过"才成立——
/// 两步验证限制这类授权之前的中间件也抛业务异常，放在前面会把它们记成授权之后的拒绝；
/// 请求此时还在租户作用域里，记录才写进操作发生的那一层。不能改成全局异常处理器：
/// 那里租户作用域已经随异常退出，租户内的失败会被写进宿主层。</para>
/// <para><b>只记业务异常。</b>参数校验失败没有业务码，无从按原因聚合；技术异常属于日志。
/// 记下错误码与消息参数（<c>LocalizationData</c>），查询时渲染出与接口报错同一句带具体值的原因；
/// 参数随记录进审计与导出，抛异常处只放可公开展示的值。</para>
/// </remarks>
public sealed class OperationFailureRecordingMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (BusinessException exception)
        {
            // 只取错误码与消息参数：基类 Exception.Data 与异常文本没有"可公开展示"的约定，不进审计
            await context.RecordFailedOperationAsync(OperationFailure.FromCode(exception.Code, exception.LocalizationData));
            throw;
        }
    }
}
