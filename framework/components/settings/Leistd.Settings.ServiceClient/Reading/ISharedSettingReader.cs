namespace Leistd.Settings.ServiceClient.Reading;

/// <summary>按当前租户读取其他服务共享的设置（只读）。</summary>
/// <remarks>
/// <para>与本地 <c>ISettingProvider</c> 是两种语义：本地读的是数据库当前值，这里新鲜期内用缓存、提供方暂不可达时用保留期内的旧值，
/// 读不到时用注册时声明的兜底值，并经 <see cref="SharedSettingValue.Source"/> 告诉调用方。除取消与调用错误外不抛异常。</para>
/// <para>Scoped，当前租户取自调用方作用域的 <c>ICurrentTenant</c>，没有租户上下文时读宿主那一份；缓存在进程内各作用域共享。</para>
/// </remarks>
public interface ISharedSettingReader
{
    /// <summary>读取一项共享设置。</summary>
    /// <param name="provider">提供方名，即注册时传给 <c>AddSharedSettingsClient</c> 的名字。</param>
    /// <param name="name">设置名，须已在注册时声明。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <exception cref="InvalidOperationException">提供方未注册，或设置名未声明。</exception>
    Task<SharedSettingValue> GetAsync(string provider, string name, CancellationToken cancellationToken = default);

    /// <summary>读取一项共享设置并转换为 <typeparamref name="T"/>；值为空时返回 <c>default</c>。</summary>
    /// <remarks>按不变文化转换，与 <c>ISettingProvider.GetAsync&lt;T&gt;</c> 相同；需要知道来源时用 <see cref="GetAsync(string, string, CancellationToken)"/>。</remarks>
    /// <typeparam name="T">目标类型，支持基元类型、枚举与带 <c>TypeConverter</c> 的类型。</typeparam>
    /// <param name="provider">提供方名。</param>
    /// <param name="name">设置名。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <exception cref="InvalidOperationException">提供方未注册，或设置名未声明。</exception>
    Task<T?> GetAsync<T>(string provider, string name, CancellationToken cancellationToken = default);
}
