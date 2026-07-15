using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Leistd.Exception.AspNetCore.Options;
using Leistd.Exception.Core;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Leistd.Exception.AspNetCore.Handlers;

public sealed class BusinessExceptionHandler(
    IOptions<GlobalExceptionOptions> options,
    IHostEnvironment environment,
    ILogger<BusinessExceptionHandler> logger,
    IProblemDetailsService problemDetailsService,
    IServiceProvider serviceProvider) : IExceptionHandler
{
    private readonly GlobalExceptionOptions _options = options.Value;

    // 可选本地化：未启用本地化（未注册 IStringLocalizer）时为 null，行为退回直出原消息 = 现状。
    private readonly IStringLocalizer? _localizer = serviceProvider.GetService<IStringLocalizer>();

    /// <summary>
    /// HTTP 状态码 → 框架通用语义键的显式映射（P3）。语义键（如 <c>Error:NotFound</c>）比
    /// 数字键（<c>Error:404</c>）对开发者/AI 更可读，且这些键在框架默认资源中真实存在。
    /// </summary>
    private static string GenericErrorKey(int statusCode) => statusCode switch
    {
        400 => "Error:BadRequest",
        401 => "Error:Unauthorized",
        403 => "Error:Forbidden",
        404 => "Error:NotFound",
        409 => "Error:Conflict",
        422 => "Error:UnprocessableEntity",
        503 => "Error:ServiceUnavailable",
        _ => "Error:InternalServer"
    };

    /// <summary>
    /// 产出用户可见消息：优先按异常 <see cref="BusinessException.LocalizationKey"/> 查资源，
    /// 无键/未命中时回落到 HTTP 通用语义键，再不行回落到异常 <c>Message</c>（日志诊断消息）。
    /// 整个过程 try/catch 隔离——本地化组件失败绝不覆盖原始业务异常（P4）。
    /// </summary>
    private string Localize(BusinessException ex, int statusCode)
    {
        // Message 始终是安全的诊断兜底（三分离：Message=日志/诊断）
        if (_localizer is null)
            return ex.Message;

        try
        {
            // 1) 有展示键 → 用键
            if (!string.IsNullOrEmpty(ex.LocalizationKey))
            {
                var byKey = _localizer[ex.LocalizationKey];
                if (!byKey.ResourceNotFound)
                    return Fill(byKey.Value, ex.LocalizationData);
            }

            // 2) 键缺失/未命中 → HTTP 通用语义键（真实存在于框架资源）
            var generic = _localizer[GenericErrorKey(statusCode)];
            if (!generic.ResourceNotFound)
                return Fill(generic.Value, ex.LocalizationData);

            // 3) 仍未命中 → 诊断消息兜底，绝不漏裸键
            return ex.Message;
        }
        catch
        {
            // 本地化组件抛异常（坏资源等）：回落诊断消息，保留原始异常上下文
            return ex.Message;
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

    /// <summary>
    /// 按状态码本地化标题；未启用本地化 / 出错 / 未命中时回退到英文默认标题。
    /// </summary>
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

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        System.Exception exception,
        CancellationToken cancellationToken)
    {
        if (!_options.Enable)
            return false;

        if (IsExcludedPath(httpContext.Request.Path))
            return false;

        var bizException = ConvertToBusinessException(exception);
        LogException(bizException, exception);

        var problemDetails = BuildProblemDetails(httpContext, bizException);

        // 设置状态码
        httpContext.Response.StatusCode = problemDetails.Status ?? StatusCodes.Status500InternalServerError;

        // 使用 IProblemDetailsService 写入响应（自动处理内容协商）
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails,
            Exception = exception
        });
    }

    private bool IsExcludedPath(PathString path)
    {
        if (_options.ExcludePatterns == null || !_options.ExcludePatterns.Any())
            return false;

        var pathValue = path.Value ?? string.Empty;
        foreach (var pattern in _options.ExcludePatterns)
        {
            if (MatchPattern(pathValue, pattern))
                return true;
        }
        return false;
    }

    private static bool MatchPattern(string path, string pattern)
    {
        if (pattern.EndsWith("/**"))
        {
            var prefix = pattern[..^3];
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        if (pattern.Contains('*'))
        {
            var regexPattern = "^" + Regex.Escape(pattern)
                .Replace("\\*\\*", ".*")
                .Replace("\\*", "[^/]*") + "$";
            return Regex.IsMatch(path, regexPattern, RegexOptions.IgnoreCase);
        }

        return path.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }

    private static BusinessException ConvertToBusinessException(System.Exception exception)
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

            // 内置归一化同样遵循三分离：Message 是英文诊断（进日志），WithLocalization 挂展示键。
            // 未启用本地化时 Message 直出；启用时按键查表（键真实存在于框架默认资源）。
            OperationCanceledException canceledException => canceledException.InnerException is TimeoutException
                ? new ServiceUnavailableException("The upstream service timed out.", canceledException)
                    .WithLocalization("Error:UpstreamTimeout")
                : new BadRequestException("The request was canceled.", canceledException)
                    .WithLocalization("Error:RequestCanceled"),

            TimeoutException timeoutException => new ServiceUnavailableException("The upstream service timed out.", timeoutException)
                .WithLocalization("Error:UpstreamTimeout"),

            HttpRequestException httpException => new ServiceUnavailableException("Failed to reach the upstream service.", httpException)
                .WithLocalization("Error:UpstreamUnavailable"),

            _ => new InternalServerException("A system error occurred. Please contact the administrator.", exception)
                .WithLocalization("Error:InternalServer")
        };
    }

    private void LogException(BusinessException bizException, System.Exception originalException)
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

    private ProblemDetails BuildProblemDetails(HttpContext httpContext, BusinessException bizException)
    {
        var statusCode = GetHttpStatusCode(bizException.Code);

        // 按当前 culture 本地化消息与标题（未启用本地化时原样返回 Message = 现状行为）
        var message = Localize(bizException, statusCode);
        var title = LocalizeTitle(statusCode);

        // 对于验证异常使用 ValidationProblemDetails
        if (bizException is UnprocessableEntityException unprocessableEntity)
        {
            var validationProblem = new ValidationProblemDetails(unprocessableEntity.ValidationErrors ?? new Dictionary<string, string[]>())
            {
                Type = GetProblemType(statusCode),
                Title = title,
                Status = statusCode,
                Detail = message,
                Instance = httpContext.Request.Path
            };
            validationProblem.Extensions["message"] = message;
            validationProblem.Extensions["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;
            validationProblem.Extensions["code"] = bizException.Code;

            if (ShouldShowDetails() && !string.IsNullOrEmpty(bizException.Details))
            {
                validationProblem.Extensions["details"] = bizException.Details;
            }

            return validationProblem;
        }

        // 标准 ProblemDetails
        var problemDetails = new ProblemDetails
        {
            Type = GetProblemType(statusCode),
            Title = title,
            Status = statusCode,
            Detail = message,
            Instance = httpContext.Request.Path
        };
        problemDetails.Extensions["message"] = message;
        problemDetails.Extensions["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;
        problemDetails.Extensions["code"] = bizException.Code;

        // 开发环境或配置显示时添加详细信息
        if (ShouldShowDetails())
        {
            var details = bizException.Details ?? bizException.GetStackTraceStr();
            if (!string.IsNullOrEmpty(details))
            {
                problemDetails.Extensions["stackTrace"] = details;
            }
        }
        else if (!string.IsNullOrEmpty(bizException.Details))
        {
            problemDetails.Extensions["details"] = bizException.Details;
        }

        return problemDetails;
    }

    private bool ShouldShowDetails()
    {
        return _options.IsShowDetails switch
        {
            true => true,
            false => false,
            null => environment.IsDevelopment()
        };
    }

    private static int GetHttpStatusCode(int errorCode)
    {
        var errorCodeStr = errorCode.ToString();
        if (errorCodeStr.Length >= 3)
        {
            var httpStatusCode = int.Parse(errorCodeStr[..3]);
            if (httpStatusCode is >= 100 and < 600)
                return httpStatusCode;
        }
        return StatusCodes.Status500InternalServerError;
    }

    private static string GetProblemType(int statusCode)
    {
        return statusCode switch
        {
            400 => "https://tools.ietf.org/html/rfc7231#section-6.5.1",
            401 => "https://tools.ietf.org/html/rfc7235#section-3.1",
            403 => "https://tools.ietf.org/html/rfc7231#section-6.5.3",
            404 => "https://tools.ietf.org/html/rfc7231#section-6.5.4",
            409 => "https://tools.ietf.org/html/rfc7231#section-6.5.8",
            422 => "https://tools.ietf.org/html/rfc4918#section-11.2",
            500 => "https://tools.ietf.org/html/rfc7231#section-6.6.1",
            _ => $"https://httpstatuses.com/{statusCode}"
        };
    }

    private static string GetProblemTitle(int statusCode)
    {
        return statusCode switch
        {
            400 => "Bad Request",
            401 => "Unauthorized",
            403 => "Forbidden",
            404 => "Not Found",
            409 => "Conflict",
            422 => "Unprocessable Entity",
            500 => "Internal Server Error",
            502 => "Bad Gateway",
            503 => "Service Unavailable",
            _ => "Error"
        };
    }
}