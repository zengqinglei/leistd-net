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
    /// **一致的** RFC 9457 / JSON:API <c>errors</c> 数组形态（每项 <c>detail</c> + <c>pointer</c>），
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

                // type 省略（RFC 9457 缺省 about:blank：无额外语义、title 用状态短语）；
                // 框架/模板不假定托管的 problem-type 文档地址，宿主如需可解析 type URI 自行设置。
                var problem = new ProblemDetails
                {
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
