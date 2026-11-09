using Leistd.ServiceClient.Options;

namespace Leistd.Settings.ServiceClient.Options;

/// <summary>读取一个提供方共享设置的客户端选项；每个提供方派生一个具体类型，默认绑定 <c>Leistd:ServiceClients:{提供方}</c>。</summary>
public class SharedSettingsClientOptions : ServiceClientOptions
{
    /// <summary>提供方 <c>MapSharedSettings</c> 路由组的相对路径，默认 <c>api/v1/internal/shared-settings</c>。</summary>
    public string Path { get; set; } = "api/v1/internal/shared-settings";

    /// <summary>新鲜期：这段时间内直接用缓存，不回源；默认 1 分钟，须大于 0。</summary>
    public TimeSpan FreshFor { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>新鲜期结束后、提供方暂不可达时还能用旧值的时长；默认 1 小时，0 表示不用旧值。</summary>
    public TimeSpan MaxStaleness { get; set; } = TimeSpan.FromHours(1);
}
