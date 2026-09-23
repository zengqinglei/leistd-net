using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Leistd.Response.AspNetCore.Writers;

// 统一响应信封作为 IProblemDetailsWriter 接入 ASP.NET Core 的问题详情管道：异常、自动模型校验、
// 状态码页、Results.Problem() 与框架各中间件的失败都经 IProblemDetailsService 写出，
// 在这一处换成信封，就不会有哪一类失败漏成另一种形状。注册时排在所有写入器之前（先到先写）。
internal sealed class ResultProblemDetailsWriter(IOptions<ProblemDetailsOptions> options) : IProblemDetailsWriter
{
    public bool CanWrite(ProblemDetailsContext context) => true;

    public ValueTask WriteAsync(ProblemDetailsContext context)
    {
        // 自定义回调由写入器负责调用；不调用就拿不到统一的 traceId 与本地化标题
        options.Value.CustomizeProblemDetails?.Invoke(context);

        var problem = context.ProblemDetails;
        var httpContext = context.HttpContext;
        var status = problem.Status ?? httpContext.Response.StatusCode;
        httpContext.Response.StatusCode = status;

        var result = ProblemDetailsEnvelope.ToResult(problem, status);
        return new ValueTask(httpContext.Response.WriteAsJsonAsync(result, httpContext.RequestAborted));
    }
}
