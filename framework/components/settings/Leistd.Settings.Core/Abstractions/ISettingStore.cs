using Leistd.Settings.Definitions;

namespace Leistd.Settings.Abstractions;

/// <summary>
/// 设置值的持久化契约，只负责按层级读写原始字符串。
/// </summary>
/// <remarks>
/// 不做定义校验与层级回落——那是 <see cref="ISettingManager"/> 与 <see cref="ISettingProvider"/> 的职责。
/// 租户隔离由实现所在的数据过滤器承担，本契约不带租户参数。
/// </remarks>
public interface ISettingStore
{
    /// <summary>读取当前租户下某一层级的全部设置值。</summary>
    /// <param name="scope">要读取的层级。</param>
    /// <param name="userId">用户级时的用户标识；租户级传 <see langword="null"/>。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<IReadOnlyDictionary<string, string>> GetAllAsync(
        SettingScopes scope,
        string? userId,
        CancellationToken cancellationToken = default);

    /// <summary>移除当前租户下的全部设置值，含各用户在该租户内的偏好。</summary>
    /// <remarks>
    /// 供永久废弃租户时清理孤儿行使用——设置行带租户归属，租户没了它们读不到也删不掉。
    /// 与本契约的其它方法一样按当前租户隐式限定：在宿主上下文调用会清掉宿主的设置。
    /// </remarks>
    /// <param name="cancellationToken">取消令牌。</param>
    Task RemoveAllAsync(CancellationToken cancellationToken = default);

    /// <summary>写入设置值；<paramref name="value"/> 为 <see langword="null"/> 时删除该层级的值。</summary>
    /// <param name="name">设置名称。</param>
    /// <param name="value">设置值；<see langword="null"/> 表示清除，使其回落到下一层。</param>
    /// <param name="scope">写入的层级。</param>
    /// <param name="userId">用户级时的用户标识；租户级传 <see langword="null"/>。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task SetAsync(
        string name,
        string? value,
        SettingScopes scope,
        string? userId,
        CancellationToken cancellationToken = default);
}
