using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Leistd.Exception.AspNetCore.Options;
using Leistd.Exception.AspNetCore.Localization;
using Leistd.Exception.Core;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Leistd.Exception.AspNetCore.Handlers;

public sealed class BusinessExceptionHandler(
    IOptions<GlobalExceptionOptions> options,
    IHostEnvironment environment,
    ILogger<BusinessExceptionHandler> logger,
    IProblemDetailsService problemDetailsService,
    IEnumerable<IExceptionResponseLocalizer> localizers) : IExceptionHandler
{
    private readonly GlobalExceptionOptions _options = options.Value;

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
                validationException.Message)
                .WithLocalization("Exception.ValidationFailed"),

            CommonException commonException => new BadRequestException(
                commonException.Message,
                commonException.InnerException),

            OperationCanceledException canceledException => canceledException.InnerException is TimeoutException
                ? new ServiceUnavailableException("The upstream service timed out. Please try again later.", canceledException)
                    .WithLocalization("Exception.UpstreamTimeout")
                : new BadRequestException("The request was canceled.", canceledException)
                    .WithLocalization("Exception.RequestCanceled"),

            TimeoutException timeoutException => new ServiceUnavailableException(
                "The request timed out. Please try again later.", timeoutException)
                .WithLocalization("Exception.RequestTimeout"),

            HttpRequestException httpException => new ServiceUnavailableException(
                $"Unable to connect to the upstream service: {httpException.Message}", httpException)
                .WithLocalization("Exception.UpstreamConnectionFailed"),

            _ => new InternalServerException("An unexpected error occurred. Please contact the administrator.", exception)
                .WithLocalization("Exception.Unhandled")
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
        var localization = LocalizeException(bizException, statusCode);
        var message = string.IsNullOrWhiteSpace(localization?.Message)
            ? bizException.Message
            : localization.Message;
        var title = string.IsNullOrWhiteSpace(localization?.Title)
            ? GetProblemTitle(statusCode)
            : localization.Title;

        // 对于验证异常使用 ValidationProblemDetails
        if (bizException is UnprocessableEntityException unprocessableEntity)
        {
            var validationErrors = localization?.Errors ?? unprocessableEntity.ValidationErrors ?? new Dictionary<string, string[]>();
            var validationProblem = new ValidationProblemDetails(validationErrors)
            {
                Type = GetProblemType(statusCode, bizException.Code),
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
            Type = GetProblemType(statusCode, bizException.Code),
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

    private ExceptionResponseLocalization? LocalizeException(BusinessException exception, int statusCode)
    {
        var localizer = localizers.LastOrDefault();
        if (localizer is null)
            return null;

        try
        {
            return localizer.Localize(exception, statusCode);
        }
        catch (System.Exception localizationException)
        {
            logger.LogWarning(
                localizationException,
                "Failed to localize BusinessException response: Code={Code}, LocalizationKey={LocalizationKey}",
                exception.Code,
                exception.LocalizationKey);
            return null;
        }
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

    private static string GetProblemType(int statusCode, int errorCode)
    {
        if (errorCode != statusCode * 100)
            return $"urn:leistd:error:{errorCode}";

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
