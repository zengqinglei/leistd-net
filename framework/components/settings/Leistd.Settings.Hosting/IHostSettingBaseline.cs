namespace Leistd.Settings.Hosting;

/// <summary>被绑定的宿主级设置在部署配置里的值，不含系统设置的覆盖。</summary>
/// <remarks>
/// 进程里的 <c>IConfiguration</c> 与 <c>IOptionsMonitor</c> 已叠加了系统设置，且可能落后于另一副本刚写入的值；
/// 设置测试这类要"用这一次读到的存储值、缺项才用部署配置"的场景从这里取部署值。机密设置同样给出：它没有代码默认值。
/// </remarks>
public interface IHostSettingBaseline
{
    /// <summary>按设置名取部署配置里的原始值；未绑定或部署里没有时为 <see langword="null"/>。</summary>
    /// <param name="settingName">被绑定的宿主级设置名。</param>
    string? GetDeploymentValue(string settingName);
}
