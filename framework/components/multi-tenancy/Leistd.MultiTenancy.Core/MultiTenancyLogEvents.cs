namespace Leistd.MultiTenancy;

/// <summary>多租户组件记录的日志事件号，号段 4300–4399；框架内各组件号段不重叠，告警按事件号匹配。</summary>
public static class MultiTenancyLogEvents
{
    /// <summary>
    /// 租户连接串解不开（Error）：密钥丢失、密钥环未共享、应用名不同或密文被篡改。
    /// 字段 <c>Purpose</c>、<c>TenantId</c>、<c>ConnectionName</c>、<c>KeyId</c>。
    /// </summary>
    public const int TenantConnectionStringUnprotectFailed = 4301;
}
