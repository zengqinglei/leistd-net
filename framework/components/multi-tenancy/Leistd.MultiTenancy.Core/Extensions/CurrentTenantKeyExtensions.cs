using Leistd.MultiTenancy.Abstractions;

namespace Leistd.MultiTenancy.Extensions;

/// <summary>
/// 把外部标识限定到当前租户。
/// </summary>
public static class CurrentTenantKeyExtensions
{
    /// <summary>宿主视角的标识前缀。</summary>
    public const string HostScope = "host";

    /// <summary>
    /// 在标识前拼上当前租户段，使同一个逻辑名在不同租户下互不可见。
    /// </summary>
    /// <remarks>
    /// <para>用于<b>租户外部</b>的命名空间：缓存键、分布式锁键这类"同一个名字在两个租户下必须是两条记录"的标识。
    /// 数据库里的租户隔离不走这里，由查询过滤器负责。</para>
    /// <para><b>不是所有缓存都该调它</b>：用来判断"你是哪个租户"的数据本身（租户注册表、租户连接配置）
    /// 在任何租户上下文下都指向同一份，加了租户段反而会按调用时机分裂成多份。这类标识保持原样。</para>
    /// <para>刻意做成显式调用而不是在某个包装类型里自动拼：自动拼就必须再补一个"本次不要拼"的开关，
    /// 调用方仍要逐处判断，却多了一层不透明。</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // 产出 "{租户 Id:N}:catalog:category:{id:N}"，交给缓存、分布式锁或幂等存储做键
    /// var key = currentTenant.ScopeKey($"catalog:category:{id:N}");
    /// </code>
    /// </example>
    /// <param name="currentTenant">当前租户上下文。</param>
    /// <param name="key">业务标识，不含租户段。</param>
    /// <returns>宿主视角为 <c>host:{key}</c>，租户视角为 <c>{租户 Id:N}:{key}</c>。</returns>
    public static string ScopeKey(this ICurrentTenant currentTenant, string key)
    {
        ArgumentNullException.ThrowIfNull(currentTenant);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        // 租户 Id 的 N 格式是 32 位十六进制，与 host 不会撞。
        return currentTenant.Id is { } tenantId
            ? $"{tenantId:N}:{key}"
            : $"{HostScope}:{key}";
    }
}
