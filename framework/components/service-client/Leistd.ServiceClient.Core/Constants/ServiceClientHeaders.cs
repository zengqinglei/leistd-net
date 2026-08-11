namespace Leistd.ServiceClient.Constants;

/// <summary>
/// 服务间调用约定的请求头名称常量。
/// </summary>
/// <remarks>
/// 这些头只承载「本次调用代表哪个用户」，其可信性由被调方的
/// <c>ServiceUserContextMiddleware</c> 基于调用方 client credentials 身份裁决；
/// 头本身不构成认证凭据。
/// </remarks>
public static class ServiceClientHeaders
{
    /// <summary>
    /// 当前用户 Id（<c>sub</c> claim 的 Guid 值）。
    /// </summary>
    public const string UserId = "X-User-Id";

    /// <summary>
    /// 当前用户名（<c>preferred_username</c> / <c>name</c> claim，值经 UTF-8 URL 编码）。
    /// </summary>
    public const string UserName = "X-User-Name";
}
