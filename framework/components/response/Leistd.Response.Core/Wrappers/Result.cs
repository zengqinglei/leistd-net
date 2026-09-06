namespace Leistd.Response.Wrappers;

/// <summary>
/// 统一响应结果：成功与业务失败共用的信封，<c>code</c> 为 0 表示成功。
/// </summary>
/// <remarks>
/// HTTP 状态码由调用方另行决定（见 <c>ControllerExtensions</c>）；本类型只承载业务语义。
/// 未包装的返回值由 <c>ResultWrapperFilter</c> 自动套上本信封。
/// </remarks>
public record Result
{
    /// <summary>业务状态码。<c>0</c> 表示成功，非零表示业务失败。</summary>
    public int Code { get; init; }

    /// <summary>面向调用方的消息；成功时通常为 <see langword="null"/>。</summary>
    public string? Message { get; init; }

    /// <summary>构造一个成功结果（<c>Code = 0</c>）。</summary>
    /// <param name="message">可选消息。</param>
    public static Result Ok(string? message = null)
        => new() { Code = 0, Message = message };

    /// <summary>构造一个业务失败结果。</summary>
    /// <param name="code">非零业务状态码。</param>
    /// <param name="message">失败消息。</param>
    public static Result Fail(int code, string message)
        => new() { Code = code, Message = message };

    /// <inheritdoc />
    public override string ToString()
    {
        return $"Response [code={Code}, message={Message}]";
    }
}
