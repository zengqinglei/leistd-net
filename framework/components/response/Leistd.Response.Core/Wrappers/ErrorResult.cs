using Leistd.ExceptionHandling;

namespace Leistd.Response.Wrappers;

/// <summary>
/// 带字段级错误明细的业务失败结果。
/// </summary>
/// <remarks>
/// 供控制器主动返回校验类失败时使用。<see cref="Errors"/> 用与异常处理组件相同的
/// <see cref="ErrorItem"/>：同一个服务不应因为开发者当时是 <c>throw</c> 还是 <c>return</c>
/// 而吐出两种字段错误形状。
/// </remarks>
public record class ErrorResult : Result
{
    /// <summary>字段级错误明细。</summary>
    public IReadOnlyList<ErrorItem>? Errors { get; init; }

    /// <summary>构造一个带字段级明细的业务失败结果。</summary>
    /// <param name="code">非零业务状态码。</param>
    /// <param name="message">失败消息。</param>
    /// <param name="errors">字段级错误明细。</param>
    public static ErrorResult Fail(int code, string message, IReadOnlyList<ErrorItem> errors)
        => new()
        {
            Code = code,
            Message = message,
            Errors = errors
        };
}
