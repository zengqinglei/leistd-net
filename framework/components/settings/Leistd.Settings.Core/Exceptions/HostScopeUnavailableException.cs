namespace Leistd.Settings.Exceptions;

/// <summary>
/// 表示在宿主上下文之外读写进程级设置。
/// </summary>
/// <remarks>
/// 进程级设置（<c>SettingScopes.Host</c>）只有宿主那一行。租户上下文下这一行不可达：
/// 查询过滤器会把它滤掉，专属库形态下连的还是租户自己的库。此时静默回落到代码默认值，
/// 读到的会是一个<b>看着有效的错误值</b>——所以这里明确抛出。
/// <para>
/// 需要在租户请求里读进程级配置的消费者，应当读进程内那份运行期状态（由宿主侧的应用器
/// 维护），而不是每个请求去问设置存储。
/// </para>
/// </remarks>
/// <param name="settingName">被访问的设置名；存储层抛出时为 <see langword="null"/>（那里只知道层级）。</param>
/// <param name="tenantId">当前租户标识；取不到时为 <see langword="null"/>。</param>
public sealed class HostScopeUnavailableException(string? settingName = null, string? tenantId = null)
    : InvalidOperationException(
        (settingName is null
            ? "Host-scoped settings can only be read or written in the host context"
            : $"Setting '{settingName}' is host-scoped and can only be read or written in the host context")
        + (tenantId is null ? "." : $"; current tenant is '{tenantId}'."))
{
    /// <summary>被访问的设置名；由存储层抛出时为 <see langword="null"/>。</summary>
    public string? SettingName { get; } = settingName;
}
