namespace Leistd.Settings.Abstractions;

/// <summary>
/// 按层级解析设置的当前生效值。
/// </summary>
/// <remarks>
/// <para>回落顺序：<b>用户级 → 租户级 → 代码默认值</b>。宿主视角走租户级那一层
/// （<c>TenantId</c> 为 <see langword="null"/> 的行），不额外引入"全局"层。</para>
/// <para>实现为 Scoped 并在一次请求内记忆化：同一请求里多次读取只查一次库，
/// 也就不存在跨节点缓存失效的问题。设置在请求之间的变更下一请求即可见。</para>
/// <para><b>进程级设置（<c>SettingScopes.Host</c>）不走这条回落链</b>：它只有宿主那一行，
/// 没有"上一层"。宿主上下文下直接读那一行，没有值才用代码默认值；租户上下文下它不可达
/// （查询过滤器会滤掉它，专属库形态下连的还是租户自己的库），此时<b>不返回代码默认值</b>——
/// 那个值看着有效，调用方分不出"这就是当前生效值"和"这一层根本读不到"。</para>
/// </remarks>
public interface ISettingProvider
{
    /// <summary>读取设置的当前生效值。</summary>
    /// <param name="name">设置名称。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <exception cref="Exceptions.UndefinedSettingException">名称未定义。</exception>
    /// <exception cref="Exceptions.HostScopeUnavailableException">
    /// 进程级设置在当前上下文不可达（非宿主上下文）。
    /// </exception>
    Task<string?> GetOrNullAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>读取设置并转换为 <typeparamref name="T"/>；值为空时返回 <c>default</c>。</summary>
    /// <typeparam name="T">目标类型，支持基元类型、枚举与带 <c>TypeConverter</c> 的类型。</typeparam>
    /// <param name="name">设置名称。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <exception cref="Exceptions.UndefinedSettingException">名称未定义。</exception>
    /// <exception cref="Exceptions.HostScopeUnavailableException">
    /// 进程级设置在当前上下文不可达；<c>default</c> 只表示"值为空"，不表示"读不到"。
    /// </exception>
    Task<T?> GetAsync<T>(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取<b>当前上下文可访问的</b>全部设置的当前生效值。
    /// </summary>
    /// <remarks>
    /// 租户上下文下不包含进程级设置：它们不属于这个租户，也读不到。不抛异常——
    /// 批量读取不该因为其中一项在当前上下文不可达而整体失败。
    /// </remarks>
    /// <param name="visibleToClientsOnly">只返回标记为可下发客户端的设置。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<IReadOnlyDictionary<string, string?>> GetAllAsync(
        bool visibleToClientsOnly = false,
        CancellationToken cancellationToken = default);
}
