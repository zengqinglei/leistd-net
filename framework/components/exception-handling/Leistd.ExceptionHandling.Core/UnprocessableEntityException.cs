using System.Text.Json;

namespace Leistd.ExceptionHandling;

/// <summary>
/// 实体验证错误异常，默认错误码 <c>Error:UnprocessableEntity</c>。
/// </summary>
/// <remarks>
/// 字段错误为结构化 <see cref="ValidationError"/>（携带 Code / Data / Message），
/// 使应用层或领域层主动抛出的字段错误无需提前拼接某种语言——由异常处理器按当前 culture 解析。
/// 处理器输出 RFC 9457 <c>ProblemDetails</c>，并用 <c>errors</c> 扩展数组承载本地化后的字段错误。
/// </remarks>
public class UnprocessableEntityException(
    IReadOnlyList<ValidationError> validationErrors,
    string message = "Validation failed.",
    Exception? innerException = null)
    : BusinessException(422, message, innerException)
{
    private readonly List<ValidationError> _validationErrors = [.. validationErrors];

    /// <summary>
    /// 结构化字段验证错误集合。
    /// </summary>
    public IReadOnlyList<ValidationError> ValidationErrors => _validationErrors;

    /// <summary>
    /// 便捷构造函数：单个字段单个错误（仅诊断消息，无错误码）。
    /// </summary>
    public UnprocessableEntityException(string field, string error, Exception? innerException = null)
        : this([new ValidationError(field, error)], "Validation failed.", innerException)
    {
    }

    /// <summary>
    /// 便捷构造函数：单个字段多个错误（仅诊断消息，无错误码）。
    /// </summary>
    public UnprocessableEntityException(string field, string[] errors, Exception? innerException = null)
        : this([.. errors.Select(e => new ValidationError(field, e))], "Validation failed.", innerException)
    {
    }

    /// <summary>
    /// 追加一条结构化字段错误。
    /// </summary>
    public UnprocessableEntityException AddError(ValidationError error)
    {
        _validationErrors.Add(error);
        return this;
    }

    /// <summary>
    /// 追加一条字段错误（诊断消息 + 可选错误码与占位参数）。
    /// </summary>
    public UnprocessableEntityException AddError(
        string field,
        string message,
        string? code = null,
        IReadOnlyDictionary<string, object?>? data = null)
    {
        _validationErrors.Add(new ValidationError(field, message, code, data));
        return this;
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return this.GetType().Name + " : [code=" + Code + ", message=" + Message + ", details=" + Details
            + ", errors=" + JsonSerializer.Serialize(ValidationErrors) + "]";
    }
}
