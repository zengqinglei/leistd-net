using System.ComponentModel.DataAnnotations;
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
    /// 按 DataAnnotations 校验对象，不通过则抛出本异常。
    /// </summary>
    /// <param name="input">待校验对象，通常是用例的入参 DTO。</param>
    /// <remarks>
    /// 给用例层兜底用：端点层是否启用了入参校验由宿主决定，组件的用例不能假设它已经跑过。
    /// 字段名按 <c>camelCase</c> 输出，与 JSON 契约一致；无字段名的对象级错误落在空字段上。
    /// </remarks>
    /// <exception cref="UnprocessableEntityException">任一 DataAnnotations 规则不通过。</exception>
    public static void ThrowIfInvalid(object input)
    {
        ArgumentNullException.ThrowIfNull(input);

        List<ValidationResult> results = [];
        if (Validator.TryValidateObject(input, new ValidationContext(input), results, validateAllProperties: true))
        {
            return;
        }

        throw new UnprocessableEntityException(
        [
            .. results.SelectMany(
                result => result.MemberNames.DefaultIfEmpty(string.Empty),
                (result, member) => new ValidationError(
                    JsonNamingPolicy.CamelCase.ConvertName(member),
                    result.ErrorMessage ?? "Invalid value."))
        ]);
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
