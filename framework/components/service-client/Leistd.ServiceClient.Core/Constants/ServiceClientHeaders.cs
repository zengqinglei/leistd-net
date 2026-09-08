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
    public const string Username = "X-Username";

    /// <summary>
    /// 当前租户 Id（租户 Guid 值）。
    /// </summary>
    /// <remarks>
    /// 值来自环境上下文 <c>ICurrentTenant</c> 而非用户 claim，因此后台任务经 <c>ICurrentTenant.Change()</c> 设定租户后也能正确传递。
    /// 被调方仅对受信服务调用把它恢复为 <c>tenant_id</c> claim；不受信来源无需剥离。
    /// </remarks>
    public const string TenantId = "X-Tenant-Id";
}
