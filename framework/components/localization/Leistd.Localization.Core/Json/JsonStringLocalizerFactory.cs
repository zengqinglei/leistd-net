using Leistd.Localization.Core.Options;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Leistd.Localization.Core.Json;

/// <summary>
/// 创建 <see cref="JsonStringLocalizer"/> 的工厂。
/// </summary>
/// <remarks>
/// 所有资源合并进单一共享字典（键为全局唯一的文案键，如 <c>Order:StockInsufficient</c>），
/// 因此不按 <c>TResourceSource</c> / baseName 分资源——<see cref="Create(Type)"/> 与
/// <see cref="Create(string, string)"/> 返回同一逻辑视图。
/// </remarks>
public sealed class JsonStringLocalizerFactory(
    JsonLocalizationResourceReader reader,
    IOptions<LeistdLocalizationOptions> options) : IStringLocalizerFactory
{
    private readonly JsonStringLocalizer _shared = new(reader, options);

    public IStringLocalizer Create(Type resourceSource) => _shared;

    public IStringLocalizer Create(string baseName, string location) => _shared;
}
