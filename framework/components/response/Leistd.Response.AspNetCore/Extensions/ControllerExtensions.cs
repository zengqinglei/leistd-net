using Leistd.ExceptionHandling;
using Microsoft.AspNetCore.Mvc;
using Leistd.Response.Wrappers;

namespace Leistd.Response.AspNetCore.Extensions;

/// <summary>
/// 提供控制器统一响应扩展。
/// </summary>
/// <remarks>
/// 错误形状二选一：要么全程抛业务异常，由全局异常处理器输出 RFC 9457 Problem Details；
/// 要么全程用 <c>FailResult</c> 输出信封。两种混用会让同一个服务出现两种错误形状——
/// 抛出的异常不会经过本类型。
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
    /// <remarks>
    /// HTTP 状态码由 <paramref name="statusCode"/> 显式给出，不从业务错误码推导。
    /// </remarks>
    public static IActionResult FailResult(this ControllerBase controller, int statusCode, int code, string message)
    {
        return controller.StatusCode(statusCode, Result.Fail(code, message));
    }

    /// <summary>
    /// 返回带字段级明细的失败响应。
    /// </summary>
    /// <param name="controller">当前控制器。</param>
    /// <param name="statusCode">HTTP 状态码。</param>
    /// <param name="code">非零业务状态码。</param>
    /// <param name="message">失败消息。</param>
    /// <param name="errors">字段级错误明细。</param>
    public static IActionResult FailResultWithErrors(
        this ControllerBase controller,
        int statusCode,
        int code,
        string message,
        IReadOnlyList<ErrorItem> errors)
    {
        return controller.StatusCode(statusCode, ErrorResult.Fail(code, message, errors));
    }
}
