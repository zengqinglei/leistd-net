namespace Leistd.EventBus.Distributed.Errors;

/// <summary>框架产生的拒收原因；传输可另行提供稳定原因码。</summary>
public static class DistributedEventRejectionReasons
{
    /// <summary>信封缺失或字段无效。</summary>
    public const string InvalidEnvelope = "invalid-envelope";

    /// <summary>消息引用的租户不存在。</summary>
    public const string UnknownTenant = "unknown-tenant";

    /// <summary>业务载荷无法反序列化或元数据无法还原。</summary>
    public const string InvalidPayload = "invalid-payload";
}
