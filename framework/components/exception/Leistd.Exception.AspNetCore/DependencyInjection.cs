using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Leistd.Exception.AspNetCore.Handlers;
using Leistd.Exception.AspNetCore.Options;
using Leistd.Exception.Core;

namespace Leistd.Exception.AspNetCore;

public static class DependencyInjection
{
    /// <summary>
    /// 让 <c>[ApiController]</c> 的自动模型校验（400）产出与业务 <see cref="UnprocessableEntityException"/>（422）
    /// **一致的** <c>errors</c> 数组——RFC 9457 Problem Details 的 Leistd 自定义扩展（每项 <c>detail</c> + <c>field</c>），
    /// 取代 ASP.NET 内置 <c>ValidationProblemDetails</c> 的 <c>{field:[string]}</c> 字典。两条校验路径形态统一。
    /// 在 <c>AddControllers()</c> 后链式调用。
    /// </summary>
    public static IMvcBuilder ConfigureApiValidation(this IMvcBuilder builder)
    {
        builder.Services.Configure<ApiBehaviorOptions>(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                // ModelState 的错误消息已由 AddDataAnnotationsLocalization 按当前 culture 本地化。
                // field 原样取 ModelState 键（如 Address.Street），与业务 422 的 ErrorItem.Field 语义一致。
                var errors = context.ModelState
                    .Where(entry => entry.Value is { Errors.Count: > 0 })
                    .SelectMany(entry => entry.Value!.Errors.Select(error => (object)new ErrorItem(
                        Detail: error.ErrorMessage,
                        Field: entry.Key,
                        Code: null,
                        LocalizationKey: null)))
                    .ToArray();

                // 校验错误定义了 errors 扩展契约（超出纯 HTTP 状态语义），据 RFC 9457 §4.2.1 不用 about:blank，
                // 而用稳定非解析 type（§3.1.1 允许）；与业务 422 共用同一 type，两条校验路径契约一致。
                var problem = new ProblemDetails
                {
                    Type = ProblemTypes.ValidationError,
                    Title = "One or more validation errors occurred.",
                    Status = StatusCodes.Status400BadRequest,
                    Instance = context.HttpContext.Request.Path
                };
                problem.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
                problem.Extensions["errors"] = errors;

                return new BadRequestObjectResult(problem)
                {
                    ContentTypes = { "application/problem+json" }
                };
            };
        });

        return builder;
    }

    public static IServiceCollection AddGlobalExceptionHandler(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 注册 ProblemDetails 服务
        services.AddProblemDetails();

        services.Configure<GlobalExceptionOptions>(
            configuration.GetSection("Leistd:GlobalException"));

        services.AddExceptionHandler<BusinessExceptionHandler>();

        return services;
    }

    public static IServiceCollection AddGlobalExceptionHandler(
        this IServiceCollection services,
        Action<GlobalExceptionOptions> configureOptions)
    {
        // 注册 ProblemDetails 服务
        services.AddProblemDetails();

        services.Configure(configureOptions);
        services.AddExceptionHandler<BusinessExceptionHandler>();

        return services;
    }

    public static IApplicationBuilder UseGlobalExceptionHandler(this IApplicationBuilder app)
    {
        return app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            AllowStatusCode404Response = true,
            ExceptionHandler = null,
            // .NET 10: 客户端主动断开（OperationCanceledException）不记录 diagnostics/ERR
            SuppressDiagnosticsCallback = ctx =>
                (ctx.Exception is OperationCanceledException && ctx.HttpContext.RequestAborted.IsCancellationRequested)
                || ctx.Exception is BusinessException
        });
    }
}
