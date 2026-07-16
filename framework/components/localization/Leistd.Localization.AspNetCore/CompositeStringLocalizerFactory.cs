using Leistd.Localization.Core.Json;
using Leistd.Localization.Core.Options;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Leistd.Localization.AspNetCore;

/// <summary>
/// 组合本地化工厂：按**显式登记的资源标记类型**路由，避免全局接管宿主既有本地化。
/// </summary>
/// <remarks>
/// 作为通用 NuGet 组件，本工厂不应把宿主所有 <see cref="IStringLocalizer{T}"/> 都吞进单张 JSON 词条表——
/// 更不应因某程序集登记了 JSON 资源，就把该程序集内所有类型（含本应走 RESX 的类型）都拉进 JSON 分支。
/// 路由规则（精确到类型）：
/// <list type="bullet">
///   <item><c>TResourceSource</c> 在 <see cref="JsonLocalizationOptions.JsonResourceTypes"/> 中 → 走
///   <see cref="JsonStringLocalizerFactory"/>（框架的全局键 JSON 词条表）。</item>
///   <item>其余（宿主 RESX、Identity/MVC 扩展、第三方库、同程序集内未登记的类型）→ 委派微软官方
///   <see cref="ResourceManagerStringLocalizerFactory"/>，继续沿用 RESX 语义。</item>
/// </list>
/// <see cref="Create(string, string)"/> 默认走官方工厂（RESX 按 baseName/location 定位）；不为 baseName 形态做 JSON 猜测。
/// 框架自身的全局 JSON 词条视图由无参 <see cref="IStringLocalizer"/> 直取 JSON 工厂提供（见 DI 注册），不经本工厂。
/// </remarks>
public sealed class CompositeStringLocalizerFactory : IStringLocalizerFactory
{
    private readonly JsonStringLocalizerFactory _json;
    private readonly IStringLocalizerFactory _fallback;
    private readonly HashSet<Type> _jsonResourceTypes;

    public CompositeStringLocalizerFactory(
        JsonStringLocalizerFactory json,
        ResourceManagerStringLocalizerFactory fallback,
        IOptions<JsonLocalizationOptions> options)
    {
        _json = json;
        _fallback = fallback;
        _jsonResourceTypes = [.. options.Value.JsonResourceTypes];
    }

    /// <summary>
    /// 按资源类型精确路由：仅显式登记为 JSON 资源的类型走 JSON，其余委派官方 RESX 工厂。
    /// </summary>
    public IStringLocalizer Create(Type resourceSource)
    {
        ArgumentNullException.ThrowIfNull(resourceSource);
        return _jsonResourceTypes.Contains(resourceSource)
            ? _json.Create(resourceSource)
            : _fallback.Create(resourceSource);
    }

    /// <summary>
    /// baseName/location 形态默认委派官方 RESX 工厂——不为其做 JSON 猜测，避免误接管宿主资源。
    /// </summary>
    public IStringLocalizer Create(string baseName, string location)
        => _fallback.Create(baseName, location);
}
