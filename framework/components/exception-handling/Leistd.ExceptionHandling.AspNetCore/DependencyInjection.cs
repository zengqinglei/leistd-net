using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Leistd.ExceptionHandling.AspNetCore.Handlers;
using Leistd.ExceptionHandling.AspNetCore.Options;
using Leistd.ExceptionHandling.AspNetCore.Constants;

namespace Leistd.ExceptionHandling.AspNetCore;

/// <summary>
/// 全局异常处理的注册与管道接入入口。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 将自动模型校验响应配置为统一的 Problem Details 格式。
    /// </summary>
    /// <remarks>在 <c>AddControllers()</c> 后调用，使 400 与业务校验的 <c>errors</c> 结构一致。</remarks>
    public static IMvcBuilder ConfigureApiValidation(this IMvcBuilder builder)
    {
        builder.Services.Configure<ApiBehaviorOptions>(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                // 保留 ModelState 键，确保 400 与业务校验使用相同的字段路径。
                var errors = context.ModelState
                    .Where(entry => entry.Value is { Errors.Count: > 0 })
                    .SelectMany(entry => entry.Value!.Errors.Select(error => (object)new ErrorItem(
                        Detail: error.ErrorMessage,
                        Field: entry.Key,
                        Code: null)))
                    .ToArray();

                // errors 是稳定扩展契约，因此使用自定义问题类型而非 about:blank。
                var problem = new ProblemDetails
                {
                    Type = ProblemTypes.ValidationError,
                    Title = "One or more validation errors occurred.",
                    Status = StatusCodes.Status400BadRequest,
                    Instance = context.HttpContext.Request.Path
                };
                problem.Extensions["traceId"] = Activity.Current?.TraceId.ToHexString()
                    ?? context.HttpContext.TraceIdentifier;
                problem.Extensions["errors"] = errors;

                return new BadRequestObjectResult(problem)
                {
                    ContentTypes = { "application/problem+json" }
                };
            };
        });

        return builder;
    }

    /// <summary>注册全局异常处理器，选项绑定自配置节。</summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">承载 <c>GlobalExceptionOptions</c> 配置节的配置根。</param>
    /// <example>
    /// <code>
    /// builder.Services.AddGlobalExceptionHandler(builder.Configuration);
    /// builder.Services.AddControllers().ConfigureApiValidation();
    ///
    /// app.UseGlobalExceptionHandler();   // 置于管道靠前位置
    ///
    /// // 业务层只按语义抛，边界统一转成 ProblemDetails
    /// throw new NotFoundException("Order 1001 not found.");
    /// </code>
    /// </example>
    public static IServiceCollection AddGlobalExceptionHandler(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddProblemDetails();

        services.Configure<GlobalExceptionOptions>(
            configuration.GetSection("Leistd:GlobalException"));

        services.AddExceptionHandler<BusinessExceptionHandler>();

        return services;
    }

    /// <summary>注册全局异常处理器，选项以委托配置。</summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configureOptions">选项配置委托。</param>
    public static IServiceCollection AddGlobalExceptionHandler(
        this IServiceCollection services,
        Action<GlobalExceptionOptions> configureOptions)
    {
        services.AddProblemDetails();

        services.Configure(configureOptions);
        services.AddExceptionHandler<BusinessExceptionHandler>();

        return services;
    }

    /// <summary>把全局异常处理接入请求管道。应尽量靠近管道前端，以覆盖后续中间件抛出的异常。</summary>
    /// <param name="app">应用构建器。</param>
    public static IApplicationBuilder UseGlobalExceptionHandler(this IApplicationBuilder app)
    {
        return app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            AllowStatusCode404Response = true,
            ExceptionHandler = null,
            // 客户端主动断开和预期业务异常不应产生框架错误诊断。
            SuppressDiagnosticsCallback = ctx =>
                (ctx.Exception is OperationCanceledException && ctx.HttpContext.RequestAborted.IsCancellationRequested)
                || ctx.Exception is BusinessException
        });
    }
}
