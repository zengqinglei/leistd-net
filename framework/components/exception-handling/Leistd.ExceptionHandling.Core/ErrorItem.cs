using System.Text.Json.Serialization;

namespace Leistd.ExceptionHandling;

/// <summary>
/// 单条字段级错误：框架内唯一的字段错误形状。
/// </summary>
/// <remarks>
/// 错误契约由本组件定义，其余组件消费同一形状：异常处理输出的 RFC 9457 Problem Details
/// <c>errors</c> 扩展项、统一响应信封 <c>ErrorResult.Errors</c>、服务客户端还原远端错误后的
/// <c>RemoteServiceException.Errors</c>。三者必须一致，否则同一个服务会因为开发者当时是
/// <c>throw</c> 还是 <c>return</c> 而吐出两种字段错误。
/// <para>属性名遵循宿主 ProblemDetails 序列化的命名策略（模板默认 camelCase →
/// <c>detail</c>/<c>field</c>/<c>code</c>），无需 <c>JsonPropertyName</c>；<c>code</c> 可空并在未设置时省略。</para>
/// </remarks>
/// <param name="Detail">本地化后的人类可读消息。</param>
/// <param name="Field">出错字段路径（业务异常为 <c>ValidationError.Field</c>，自动模型校验为 MVC ModelState 键，如 <c>Address.Street</c>）。</param>
/// <param name="Code">错误码，同时是展示词条键，前端可据此分支；可空，仅当抛出方设置时才有值。Leistd 自定义扩展。</param>
public sealed record ErrorItem(
    string Detail,
    string Field,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Code);
