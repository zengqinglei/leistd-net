using Leistd.ExceptionHandling;
using Leistd.OperationRecords.AspNetCore.Extensions;
using Leistd.OperationRecords.Models;

namespace CompanyName.ProjectName.Api.Middlewares;

/// <summary>
/// 授权通过之后被业务规则拒绝的写操作，补一条失败的操作记录；异常原样重抛，响应仍由全局异常处理写出。
/// </summary>
/// <remarks>
/// <para><b>补的是组件端点的缺口。</b>权限管理这类由组件映射的端点，业务拒绝（并发冲突、权限未定义、
/// 主体不存在）发生在组件内部，这里没有代码能在拒绝处调记录器；被拒那一半由
/// <see cref="Auth.ApiAuthorizationResultHandler"/> 补记，这里补另一半。
/// 记不记仍由端点上的 <c>[OperationRecordAction]</c> 决定，没打注解就什么都不做。</para>
/// <para><b>必须紧接 <c>UseAuthorization()</c>。</b>授权没通过的请求到不了这里，"授权已通过"才成立——
/// 两步验证限制这类授权之前的中间件也抛业务异常，放在前面会把它们记成授权之后的拒绝；
/// 请求此时还在租户作用域里，记录才写进操作发生的那一层。不能改成全局异常处理器：
/// 那里租户作用域已经随异常退出，租户内的失败会被写进宿主层。</para>
/// <para><b>只记业务异常。</b>参数校验失败没有业务码，无从按原因聚合；技术异常属于日志。
/// 只带错误码：异常的占位参数可能含用户提交的原值，进了记录就会随导出带离系统。</para>
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
            await context.RecordFailedOperationAsync(OperationFailure.FromCode(exception.Code));
            throw;
        }
    }
}
