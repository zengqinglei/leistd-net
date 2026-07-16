using System.Text.Json;

namespace Leistd.Exception.Core;

/// <summary>
/// 实体验证错误异常，默认错误码：42200。
/// </summary>
/// <remarks>
/// 字段错误为结构化 <see cref="ValidationError"/>（携带 Code / LocalizationKey / Data / Message），
/// 使应用层或领域层主动抛出的字段错误无需提前拼接某种语言——由异常处理器按当前 culture 解析。
/// 处理器仍按 RFC 7807 <c>ValidationProblemDetails</c> 形状（<c>errors: { field: [string] }</c>）输出本地化后的文案。
/// </remarks>
public class UnprocessableEntityException(
    IReadOnlyList<ValidationError> validationErrors,
    string message = "Validation failed.",
    System.Exception? innerException = null)
    : BusinessException("422", message, innerException)
{
    private readonly List<ValidationError> _validationErrors = [.. validationErrors];

    /// <summary>
    /// 结构化字段验证错误集合。
    /// </summary>
    public IReadOnlyList<ValidationError> ValidationErrors => _validationErrors;

    /// <summary>
    /// 便捷构造函数：单个字段单个错误（仅诊断消息，无本地化键）。
    /// </summary>
    public UnprocessableEntityException(string field, string error, System.Exception? innerException = null)
        : this([new ValidationError(field, error)], "Validation failed.", innerException)
    {
    }

    /// <summary>
    /// 便捷构造函数：单个字段多个错误（仅诊断消息，无本地化键）。
    /// </summary>
    public UnprocessableEntityException(string field, string[] errors, System.Exception? innerException = null)
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
    /// 追加一条字段错误（诊断消息 + 可选本地化键与占位参数）。
    /// </summary>
    public UnprocessableEntityException AddError(
        string field,
        string message,
        string? code = null,
        string? localizationKey = null,
        IReadOnlyDictionary<string, object?>? data = null)
    {
        _validationErrors.Add(new ValidationError(field, message, code, localizationKey, data));
        return this;
    }

    public override string ToString()
    {
        return this.GetType().Name + " : [code=" + Code + ", message=" + Message + ", details=" + Details
            + ", errors=" + JsonSerializer.Serialize(ValidationErrors) + "]";
    }
}
