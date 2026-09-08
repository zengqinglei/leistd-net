using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Leistd.ExceptionHandling.AspNetCore.Options;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Leistd.Exceptions;
using Leistd.ExceptionHandling.AspNetCore.Constants;
using Leistd.ExceptionHandling.Constants;

namespace Leistd.ExceptionHandling.AspNetCore.Handlers;

/// <summary>
/// 全局异常处理器：把未捕获异常转成 RFC 9457 ProblemDetails 响应。
/// </summary>
/// <remarks>
/// 由 <c>AddGlobalExceptionHandler()</c> 注册；先归一化异常，再生成响应。
/// 禁用处理器或路径命中 <c>ExcludePatterns</c> 时交回宿主处理。
/// </remarks>
public sealed class BusinessExceptionHandler(
    IOptionsMonitor<GlobalExceptionOptions> optionsMonitor,
    ILogger<BusinessExceptionHandler> logger,
    IProblemDetailsService problemDetailsService,
    IServiceProvider serviceProvider) : IExceptionHandler
{
    // 本地化可选；缺失时遵循安全回退规则。
    private readonly IStringLocalizer? _localizer = serviceProvider.GetService<IStringLocalizer>();

    // 依次按异常码、状态码和最终回退规则生成用户消息。
    // 本地化失败不能覆盖原始业务异常。
    private string Localize(BusinessException ex, GlobalExceptionOptions options)
    {
        if (_localizer is null)
            return LastResort(ex, options);

        try
        {
            var byCode = _localizer[ex.Code];
            if (!byCode.ResourceNotFound)
                return Fill(byCode.Value, ex.LocalizationData);

            var genericCode = GenericErrorCodes.ForStatus(ex.StatusCode);
            if (!string.Equals(ex.Code, genericCode, StringComparison.Ordinal))
            {
                var generic = _localizer[genericCode];
                if (!generic.ResourceNotFound)
                    return Fill(generic.Value, ex.LocalizationData);
            }

            return LastResort(ex, options);
        }
        catch
        {
            return LastResort(ex, options);
        }
    }

    // 默认只公开显式标记的用户消息，其余异常以状态短语兜底。
    private static string LastResort(BusinessException ex, GlobalExceptionOptions options)
        => options.FallbackToExceptionMessage || ex.IsUserFacingMessage
            ? ex.Message
            : GetProblemTitle(ex.StatusCode);

    // 每个字段错误同时携带本地化消息、字段名和可选机器码。
    private ErrorItem[] BuildErrorItems(UnprocessableEntityException exception)
    {
        return [.. exception.ValidationErrors.Select(error => new ErrorItem(
            Detail: LocalizeValidationError(error),
            Field: error.Field,
            Code: error.Code))];
    }

    private string LocalizeValidationError(ValidationError error)
    {
        if (_localizer is null || string.IsNullOrEmpty(error.Code))
            return error.Message;

        try
        {
            var localized = _localizer[error.Code];
            return localized.ResourceNotFound ? error.Message : Fill(localized.Value, error.Data);
        }
        catch
        {
            return error.Message;
        }
    }

    private static string Fill(string text, IReadOnlyDictionary<string, object?>? data)
    {
        if (data is not { Count: > 0 })
            return text;
        foreach (var pair in data)
            text = text.Replace("{" + pair.Key + "}", pair.Value?.ToString() ?? string.Empty, StringComparison.Ordinal);
        return text;
    }

    // 按状态码本地化标题；未启用本地化 / 出错 / 未命中时回退到英文默认标题。
    private string LocalizeTitle(int statusCode)
    {
        var fallback = GetProblemTitle(statusCode);
        if (_localizer is null)
            return fallback;

        try
        {
            var localized = _localizer["Title:" + statusCode];
            return localized.ResourceNotFound ? fallback : localized.Value;
        }
        catch
        {
            return fallback;
        }
    }

    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var options = optionsMonitor.CurrentValue;
        if (!options.Enabled)
            return false;

        if (IsExcludedPath(httpContext.Request.Path, options.ExcludePatterns))
            return false;

        var bizException = ConvertToBusinessException(exception);
        LogException(bizException, exception);

        var problemDetails = BuildProblemDetails(httpContext, bizException, exception, options);

        httpContext.Response.StatusCode = problemDetails.Status ?? StatusCodes.Status500InternalServerError;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails,
            Exception = exception
        });
    }

    private static bool IsExcludedPath(PathString path, IReadOnlySet<string> excludePatterns)
    {
        if (excludePatterns.Count == 0)
            return false;

        var pathValue = path.Value ?? string.Empty;
        foreach (var pattern in excludePatterns)
        {
            if (MatchPattern(pathValue, pattern))
                return true;
        }
        return false;
    }

    // 配置模式在每个请求上复用已编译的正则。
    private static readonly ConcurrentDictionary<string, Regex> PatternCache = new();

    private static bool MatchPattern(string path, string pattern)
    {
        // 路径结构比较不能受当前区域性影响。
        if (pattern.EndsWith("/**", StringComparison.Ordinal))
        {
            var prefix = pattern[..^3];
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        if (pattern.Contains('*'))
        {
            var regex = PatternCache.GetOrAdd(pattern, static p => new Regex(
                "^" + Regex.Escape(p).Replace("\\*\\*", ".*").Replace("\\*", "[^/]*") + "$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled));

            return regex.IsMatch(path);
        }

        return path.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }

    private static BusinessException ConvertToBusinessException(Exception exception)
    {
        return exception switch
        {
            BusinessException businessException => businessException,

            ValidationException validationException => new UnprocessableEntityException(
                validationException.ValidationResult?.MemberNames?.FirstOrDefault() ?? "unknown",
                validationException.Message),

            CommonException commonException => new BadRequestException(
                commonException.Message,
                commonException.InnerException),

            // Message 用于诊断，错误码同时作为展示词条键。
            OperationCanceledException canceledException => canceledException.InnerException is TimeoutException
                ? new ServiceUnavailableException("The upstream service timed out.", canceledException)
                    .WithCode("Error:UpstreamTimeout")
                : new BadRequestException("The request was canceled.", canceledException)
                    .WithCode("Error:RequestCanceled"),

            TimeoutException timeoutException => new ServiceUnavailableException("The upstream service timed out.", timeoutException)
                .WithCode("Error:UpstreamTimeout"),

            HttpRequestException httpException => new ServiceUnavailableException("Failed to reach the upstream service.", httpException)
                .WithCode("Error:UpstreamUnavailable"),

            _ => new InternalServerException("A system error occurred. Please contact the administrator.", exception)
                .WithCode("Error:InternalServer")
        };
    }

    private void LogException(BusinessException bizException, Exception originalException)
    {
        if (bizException is InternalServerException)
        {
            logger.LogError(originalException,
                "InternalServerException: Code={Code}, Message={Message}",
                bizException.Code, bizException.Message);
        }
        else
        {
            logger.LogWarning(
                "BusinessException: {ExceptionType}, Code={Code}, Message={Message}",
                bizException.GetType().Name, bizException.Code, bizException.Message);
        }
    }

    private ProblemDetails BuildProblemDetails(
        HttpContext httpContext,
        BusinessException bizException,
        Exception originalException,
        GlobalExceptionOptions options)
    {
        var statusCode = bizException.StatusCode;

        var message = Localize(bizException, options);
        var title = LocalizeTitle(statusCode);

        // 验证错误使用可携带机器码的 Leistd errors 扩展，并共享稳定的类型 URI。
        if (bizException is UnprocessableEntityException unprocessableEntity)
        {
            var validationProblem = new ProblemDetails
            {
                Type = ProblemTypes.ValidationError,
                Title = title,
                Status = statusCode,
                Detail = message,
                Instance = httpContext.Request.Path
            };
            validationProblem.Extensions["message"] = message;
            validationProblem.Extensions["traceId"] = ResolveTraceId(httpContext);
            validationProblem.Extensions["code"] = bizException.Code;
            validationProblem.Extensions["errors"] = BuildErrorItems(unprocessableEntity);

            AddDiagnosticExtensions(validationProblem, bizException, originalException, options);

            return validationProblem;
        }

        // 业务错误携带稳定机器码，因此使用定义明确的类型 URI。
        var problemDetails = new ProblemDetails
        {
            Type = ProblemTypes.BusinessError,
            Title = title,
            Status = statusCode,
            Detail = message,
            Instance = httpContext.Request.Path
        };
        problemDetails.Extensions["message"] = message;
        problemDetails.Extensions["traceId"] = ResolveTraceId(httpContext);
        problemDetails.Extensions["code"] = bizException.Code;

        AddDiagnosticExtensions(problemDetails, bizException, originalException, options);

        return problemDetails;
    }

    private static void AddDiagnosticExtensions(
        ProblemDetails problemDetails,
        BusinessException businessException,
        Exception originalException,
        GlobalExceptionOptions options)
    {
        if (!options.IncludeExceptionDetails)
        {
            return;
        }

        if (!string.IsNullOrEmpty(businessException.Details))
        {
            problemDetails.Extensions["details"] = businessException.Details;
        }

        if (!string.IsNullOrEmpty(originalException.StackTrace))
        {
            problemDetails.Extensions["stackTrace"] = originalException.StackTrace;
        }
    }

    // Activity.TraceId 标识整条链路；无 Activity 时回退到中间件同步的请求标识。
    private static string ResolveTraceId(HttpContext httpContext)
        => Activity.Current?.TraceId.ToHexString() ?? httpContext.TraceIdentifier;

    private static string GetProblemTitle(int statusCode)
    {
        return statusCode switch
        {
            400 => "Bad Request",
            401 => "Unauthorized",
            403 => "Forbidden",
            404 => "Not Found",
            409 => "Conflict",
            415 => "Unsupported Media Type",
            422 => "Unprocessable Entity",
            500 => "Internal Server Error",
            502 => "Bad Gateway",
            503 => "Service Unavailable",
            _ => "Error"
        };
    }
}
