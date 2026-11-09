using Leistd.Settings.ServiceClient.Options;

namespace Leistd.Settings.ServiceClient.Reading;

// 注册时登记的一个提供方：声明的设置与兜底值、选项与回源入口（按 TOptions 闭合，读取器不必知道具体类型）
internal sealed record SharedSettingProviderRegistration(
    string Name,
    IReadOnlyDictionary<string, string?> Fallbacks,
    Func<IServiceProvider, SharedSettingsClientOptions> GetOptions,
    Func<IServiceProvider, string, string?, CancellationToken, Task<HttpResponseMessage>> Fetch);
