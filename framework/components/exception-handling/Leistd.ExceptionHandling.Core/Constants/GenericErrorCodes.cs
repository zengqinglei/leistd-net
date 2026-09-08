namespace Leistd.ExceptionHandling.Constants;

/// <summary>
/// 提供按 HTTP 状态码映射的通用错误码。
/// </summary>
/// <remarks>
/// 业务异常未显式 <c>WithCode(...)</c> 时取这里的值作为默认错误码，
/// HTTP 边界也用它作为具体键未命中时的第二级回落。两处共用一份映射，避免各写一份而漂移。
/// 这些键的默认中英文案随 <c>Leistd.Localization.Core</c> 分发。
/// </remarks>
public static class GenericErrorCodes
{
    /// <summary>
    /// 获取指定状态码的通用错误码。
    /// </summary>
    public static string ForStatus(int statusCode) => statusCode switch
    {
        400 => "Error:BadRequest",
        401 => "Error:Unauthorized",
        403 => "Error:Forbidden",
        404 => "Error:NotFound",
        409 => "Error:Conflict",
        415 => "Error:UnsupportedMediaType",
        422 => "Error:UnprocessableEntity",
        502 => "Error:BadGateway",
        503 => "Error:ServiceUnavailable",
        _ => "Error:InternalServer"
    };
}
