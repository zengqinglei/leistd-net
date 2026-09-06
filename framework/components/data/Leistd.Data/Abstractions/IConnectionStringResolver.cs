using Leistd.Data.Attributes;
using Leistd.Data.Constants;

namespace Leistd.Data.Abstractions;

/// <summary>
/// 按连接名称解析最终连接字符串。
/// </summary>
/// <remarks>
/// <para><b>本抽象与多租户无关</b>，刻意放在叶子包里：解析"这个 DbContext 该连哪个库"是持久化
/// 层的通用需求，多租户只是其中一种实现。若把抽象放在多租户组件里，任何用到工作单元 + EF Core
/// 的服务都会被迫依赖多租户——包括根本不分租户的服务。</para>
/// <para>租户感知的实现由多租户侧提供；未注册任何实现时，DbContext 保持宿主配置的单连接行为。</para>
/// </remarks>
public interface IConnectionStringResolver
{
    /// <summary>解析指定名称的最终连接字符串。</summary>
    /// <param name="connectionStringName">
    /// 连接名，来自 DbContext 上的 <see cref="ConnectionStringNameAttribute"/>；
    /// 未声明时为 <see cref="ConnectionStringNames.Default"/>
    /// </param>
    /// <remarks>
    /// <b>契约只有两条</b>：给定连接名返回一个非空连接字符串；解析不出就抛异常（空串或空白按失败处理）。
    /// <b>实现必须失败关闭</b>：配置缺失、依赖不可达、凭据无法解析都要抛，
    /// 不得静默回退到某个"默认"连接——回退意味着数据写到了调用方并未选择的库。
    /// 本抽象不描述任何解析策略；租户感知实现的策略见 multi-tenancy 组件文档。
    /// </remarks>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>最终连接字符串；解析不出时抛异常，不返回 null 或空串</returns>
    Task<string> ResolveAsync(
        string connectionStringName,
        CancellationToken cancellationToken = default);
}
