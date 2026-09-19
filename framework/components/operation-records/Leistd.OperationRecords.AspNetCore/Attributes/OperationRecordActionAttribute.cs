namespace Leistd.OperationRecords.AspNetCore.Attributes;

/// <summary>
/// 声明写端点对应的业务动作码，让<b>授权阶段的拒绝</b>也能落一条失败记录。
/// </summary>
/// <remarks>
/// <para><c>[Authorize(Policy = ...)]</c> 的拒绝发生在授权阶段，请求根本到不了应用服务，
/// 那里的记录调用看不见它。结果是：被正常拒绝的请求全都没有痕迹，而"谁在反复尝试他没有的权限"
/// 恰恰是审计最该回答的问题之一。这个特性把动作码带到端点元数据上，供
/// <c>HttpContext.RecordDeniedOperationAsync()</c> 在拒绝时读取。</para>
/// <para><b>动作码显式声明、不从路由推断</b>：路由会改而"发生过什么"不该随之改写，
/// 且被拒记录必须与成功路径写下的动作码逐字一致，否则按动作检索只能查到一半。</para>
/// </remarks>
/// <param name="action">业务动作码，与应用服务成功路径使用的值逐字一致。</param>
/// <param name="targetRouteKeys">
/// 目标标识所在的路由参数名，按顺序以 <c>/</c> 连接；创建类端点尚无目标标识时省略，记为 <c>-</c>。
/// </param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class OperationRecordActionAttribute(string action, params string[] targetRouteKeys) : Attribute
{
    /// <summary>业务动作码。</summary>
    public string Action { get; } = action;

    /// <summary>
    /// 目标标识的路由参数名，按顺序以 <c>/</c> 连接；为空表示该端点没有目标标识。
    /// </summary>
    /// <remarks>
    /// <b>允许多个键</b>是因为目标标识不总是一个值：按 <c>(租户, 连接名)</c> 逐行登记的资源，
    /// 目标标识是 <c>{tenantId}/{name}</c>，两段都在路由上。只取第一段会让被拒记录与成功路径
    /// 的写法分叉，按目标检索就只能查到一半——而且不会报任何错。
    /// </remarks>
    public IReadOnlyList<string> TargetRouteKeys { get; } = targetRouteKeys;

    /// <summary>
    /// 加在路由值前面的前缀，让被拒记录的目标标识与成功路径写法一致（如权限授予记为 <c>Role/{roleId}</c>）。
    /// </summary>
    public string TargetIdPrefix { get; set; } = string.Empty;
}
