namespace Leistd.Settings.Abstractions;

/// <summary>
/// 按层级解析设置的当前生效值。
/// </summary>
/// <remarks>
/// <para>回落顺序：<b>用户级 → 租户级 → 代码默认值</b>。宿主视角走租户级那一层
/// （<c>TenantId</c> 为 <see langword="null"/> 的行），不额外引入"全局"层。</para>
/// <para>实现为 Scoped 并在一次请求内记忆化：同一请求里多次读取只查一次库，
/// 也就不存在跨节点缓存失效的问题。设置在请求之间的变更下一请求即可见。</para>
/// </remarks>
public interface ISettingProvider
{
    /// <summary>读取设置的当前生效值；未定义的名称抛 <c>UndefinedSettingException</c>。</summary>
    /// <param name="name">设置名称。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<string?> GetOrNullAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>读取设置并转换为 <typeparamref name="T"/>；值为空时返回 <c>default</c>。</summary>
    /// <typeparam name="T">目标类型，支持基元类型、枚举与带 <c>TypeConverter</c> 的类型。</typeparam>
    /// <param name="name">设置名称。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<T?> GetAsync<T>(string name, CancellationToken cancellationToken = default);

    /// <summary>读取全部设置的当前生效值。</summary>
    /// <param name="visibleToClientsOnly">只返回标记为可下发客户端的设置。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<IReadOnlyDictionary<string, string?>> GetAllAsync(
        bool visibleToClientsOnly = false,
        CancellationToken cancellationToken = default);
}
