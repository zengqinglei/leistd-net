using Leistd.ExceptionHandling;
using Leistd.ExceptionHandling.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Leistd.Response.Wrappers;

namespace Leistd.Response.AspNetCore.Extensions;

/// <summary>
/// 提供控制器统一响应扩展。
/// </summary>
/// <remarks>
/// 启用 <c>AddResponseWrapper()</c> 后，手工失败、异常与自动模型校验共用数字信封；
/// 未启用时，异常处理器保持默认的 Problem Details，此时不应混用手工信封。
/// </remarks>
public static class ControllerExtensions
{
    /// <summary>
    /// 返回带数据的成功响应。
    /// </summary>
    public static IActionResult OkResult<T>(this ControllerBase controller, T data, string? message = null)
    {
        return controller.Ok(Result<T>.Ok(data, message));
    }

    /// <summary>
    /// 返回无数据的成功响应。
    /// </summary>
    public static IActionResult OkResult(this ControllerBase controller, string? message = null)
    {
        return controller.Ok(Result.Ok(message));
    }

    /// <summary>
    /// 返回失败响应。
    /// </summary>
    /// <param name="controller">当前控制器。</param>
    /// <param name="statusCode">HTTP 状态码；信封式契约要求恒为 200 时直接传 200。</param>
    /// <param name="code">非零业务状态码。</param>
    /// <param name="message">失败消息。</param>
    /// <param name="errorCode">可选的稳定字符串业务码。</param>
    /// <remarks>
    /// HTTP 状态码由 <paramref name="statusCode"/> 显式给出，不从业务错误码推导。
    /// </remarks>
    public static IActionResult FailResult(this ControllerBase controller, int statusCode, int code, string message,
        string? errorCode = null)
    {
        return controller.StatusCode(statusCode, Result.Fail(code, message) with
        {
            TraceId = RequestTraceId.Get(controller.HttpContext),
            ErrorCode = errorCode
        });
    }

    /// <summary>
    /// 返回带字段级明细的失败响应。
    /// </summary>
    /// <param name="controller">当前控制器。</param>
    /// <param name="statusCode">HTTP 状态码。</param>
    /// <param name="code">非零业务状态码。</param>
    /// <param name="message">失败消息。</param>
    /// <param name="errors">字段级错误明细。</param>
    /// <param name="errorCode">可选的稳定字符串业务码。</param>
    public static IActionResult FailResultWithErrors(
        this ControllerBase controller,
        int statusCode,
        int code,
        string message,
        IReadOnlyList<ErrorItem> errors,
        string? errorCode = null)
    {
        return controller.StatusCode(statusCode, ErrorResult.Fail(code, message, errors) with
        {
            TraceId = RequestTraceId.Get(controller.HttpContext),
            ErrorCode = errorCode
        });
    }
}
