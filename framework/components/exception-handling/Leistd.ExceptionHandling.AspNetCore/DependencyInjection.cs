using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Leistd.ExceptionHandling.AspNetCore.Handlers;
using Leistd.ExceptionHandling.AspNetCore.Diagnostics;
using Leistd.ExceptionHandling.AspNetCore.Options;
using Leistd.ExceptionHandling.AspNetCore.Constants;
using Leistd.ExceptionHandling.AspNetCore.Descriptors;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Leistd.ExceptionHandling.AspNetCore;

/// <summary>
/// 全局异常处理的注册与管道接入入口。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 将自动模型校验响应接入统一失败管道：经 <see cref="IProblemDetailsService"/> 写出，默认为 Problem Details。
    /// </summary>
    /// <remarks>
    /// <para>在 <c>AddControllers()</c> 后调用，使 400 与业务校验的 <c>errors</c> 结构一致。</para>
    /// <para><c>errors[].field</c> 跟随宿主的 JSON 命名策略（请求体里叫 <c>name</c>，这里就是 <c>name</c>），
    /// 调用方据此把错误落回对应的输入项；默认 Problem Details 的标题按 <c>Title:{状态码}</c> 本地化。</para>
    /// <para>字段名换算只作用于属性式 DTO（<c>{ get; init; }</c>）。位置记录（<c>record X([Required] string Name)</c>）
    /// 的校验键取自构造参数，ASP.NET 不对它应用命名策略，仍是 C# 参数名——输入 DTO 应写成属性式。</para>
    /// </remarks>
    public static IMvcBuilder ConfigureApiValidation(this IMvcBuilder builder)
    {
        if (builder.Services.Any(service => service.ServiceType == typeof(ApiValidationRegistrationMarker)))
            return builder;
        builder.Services.AddSingleton<ApiValidationRegistrationMarker>();
        builder.Services.AddProblemDetails();
        RegisterProblemDetailsConventions(builder.Services);

        // 模型校验的键默认是 C# 属性名，而请求体与显式验证的字段名都按 JSON 命名策略写——
        // 同一个字段两种叫法，调用方只能大小写不敏感地去猜。宿主没设命名策略时属性名即 JSON 名，无需处理。
        builder.Services.AddOptions<MvcOptions>()
            .Configure<IOptions<JsonOptions>>((mvcOptions, jsonOptions) =>
            {
                if (jsonOptions.Value.JsonSerializerOptions.PropertyNamingPolicy is { } namingPolicy)
                {
                    mvcOptions.ModelMetadataDetailsProviders.Add(
                        new SystemTextJsonValidationMetadataProvider(namingPolicy));
                }
            });

        // 请求体读不成 JSON 时，System.Text.Json 的异常消息（行号、字节位置、内部路径）默认会写进字段错误，
        // 原样回给调用方。这是协议层失败，调用方只需知道哪个字段读不成；与 Minimal API 路径不带解析细节一致。
        builder.Services.Configure<JsonOptions>(options => options.AllowInputFormatterExceptionMessages = false);

        builder.Services.PostConfigure<ApiBehaviorOptions>(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                var errors = context.ModelState
                    .Where(entry => entry.Value is { Errors.Count: > 0 })
                    .SelectMany(entry => entry.Value!.Errors.Select(error => new ErrorItem(
                        // 只有异常、没有文案的错误（如读不成 JSON）与 MVC 自带的校验问题同样回落到通用句
                        Detail: string.IsNullOrEmpty(error.ErrorMessage) ? "The input was not valid." : error.ErrorMessage,
                        Field: entry.Key,
                        Code: null)))
                    .ToArray();
                var localizer = context.HttpContext.RequestServices.GetService<IStringLocalizer>();
                // 输入校验是协议层失败：只带字段错误与本地化标题，不合成业务码
                var descriptor = new ExceptionDescriptor(StatusCodes.Status400BadRequest, Errors: errors);
                // 未启用本地化时沿用 ASP.NET Core 校验问题的惯用标题
                return new ProblemDetailsActionResult(ExceptionProblemDetails.Create(
                    context.HttpContext, descriptor, localizer, titleFallback: "One or more validation errors occurred."));
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
    /// // 业务层只提供稳定错误码与安全默认文案
    /// throw new BusinessException("Order:NotFound", "The order was not found.");
    /// </code>
    /// </example>
    public static IServiceCollection AddGlobalExceptionHandler(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddProblemDetails();
        RegisterProblemDetailsConventions(services);

        services.Configure<GlobalExceptionOptions>(
            configuration.GetSection("Leistd:GlobalException"));

        services.AddExceptionHandler<BusinessExceptionHandler>();

        return services;
    }

    /// <summary>注册全局异常处理器，先绑定配置节，再应用宿主的编程式扩展。</summary>
    public static IServiceCollection AddGlobalExceptionHandler(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<GlobalExceptionOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(configureOptions);
        services.AddGlobalExceptionHandler(configuration);
        services.Configure(configureOptions);
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
        RegisterProblemDetailsConventions(services);

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
            // 处理器放行的异常由本中间件经 IProblemDetailsService 写出，状态码取自这里。
            // .NET 10 的默认值一律 500；框架判定的请求错误（BadHttpRequestException）自带状态码，沿用它
            // （ASP.NET Core 主干已内置同一规则），与生产环境不抛异常、由状态码页写出的 400 同形。
            StatusCodeSelector = exception => exception is BadHttpRequestException badRequest
                ? badRequest.StatusCode
                : StatusCodes.Status500InternalServerError,
            // 客户端主动断开和预期业务异常不应产生框架错误诊断。
            SuppressDiagnosticsCallback = ctx =>
                (ctx.Exception is OperationCanceledException && ctx.HttpContext.RequestAborted.IsCancellationRequested)
                || ctx.Exception is BusinessException
                // 框架判定的请求错误是客户端问题，框架自己已记 Debug 日志
                || ctx.Exception is BadHttpRequestException
        });
    }

    private static void RegisterProblemDetailsConventions(IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<ProblemDetailsOptions>,
            ProblemDetailsConventionsPostConfigure>());
    }

    // 所有写出的问题详情（异常、自动校验、状态码页、Results.Problem、框架各中间件）都经各写入器调用这里：
    // 在唯一的钩子上补齐本框架的约定，而不是为某一类失败另造一条写出路径。
    private sealed class ProblemDetailsConventionsPostConfigure : IPostConfigureOptions<ProblemDetailsOptions>
    {
        // ASP.NET Core 对 500 给的默认标题不是状态短语
        private const string DefaultServerErrorTitle = "An error occurred while processing your request.";

        public void PostConfigure(string? name, ProblemDetailsOptions options)
        {
            var customize = options.CustomizeProblemDetails;
            options.CustomizeProblemDetails = context =>
            {
                customize?.Invoke(context);
                var problem = context.ProblemDetails;
                // 默认写入器会用 Activity.Id 无条件重写 traceId；恢复请求入口选定的标识，
                // 避免失败响应与关联 ID 响应头、日志分叉。
                problem.Extensions["traceId"] = RequestTraceId.Get(context.HttpContext);

                // 框架只给了状态码的问题详情，标题是英文默认值：按 Title:{状态码} 本地化。
                // 调用方自己写的标题不动。
                var status = problem.Status ?? context.HttpContext.Response.StatusCode;
                if (problem.Title is null
                    || problem.Title == ReasonPhrases.GetReasonPhrase(status)
                    || problem.Title == DefaultServerErrorTitle)
                {
                    problem.Title = ProblemTitles.Localize(
                        context.HttpContext.RequestServices.GetService<IStringLocalizer>(), status);
                }
            };
        }
    }

    // 自动模型校验的响应经统一管道写出；MVC 的 ObjectResult 走输出格式化器，不经过 IProblemDetailsService，
    // 用它会让启用信封的宿主在 400 上冒出一份 Problem Details。
    private sealed class ProblemDetailsActionResult(ProblemDetails problem) : IActionResult
    {
        public Task ExecuteResultAsync(ActionContext context)
        {
            var httpContext = context.HttpContext;
            httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status400BadRequest;
            return httpContext.RequestServices.GetRequiredService<IProblemDetailsService>()
                .WriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem })
                .AsTask();
        }
    }

    private sealed class ApiValidationRegistrationMarker { }
}
