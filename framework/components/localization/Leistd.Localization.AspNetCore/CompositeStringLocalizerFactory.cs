using System.Reflection;
using Leistd.Localization.Core.Json;
using Leistd.Localization.Core.Options;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Leistd.Localization.AspNetCore;

/// <summary>
/// 组合本地化工厂：按资源来源路由，避免全局接管宿主既有本地化。
/// </summary>
/// <remarks>
/// 作为通用 NuGet 组件，本工厂不应把宿主所有 <see cref="IStringLocalizer{T}"/> 都吞进单张 JSON 词条表。
/// 路由规则：
/// <list type="bullet">
///   <item>资源类型所属程序集在 <see cref="JsonLocalizationOptions.ResourceAssemblies"/> 中（即调用方显式登记了 JSON 资源）→ 走
///   <see cref="JsonStringLocalizerFactory"/>（框架的全局键 JSON 词条表）。</item>
///   <item>其余（宿主 RESX、Identity/MVC 扩展、第三方库的 typed localizer）→ 委派微软官方
///   <see cref="ResourceManagerStringLocalizerFactory"/>，继续沿用 RESX 语义。</item>
/// </list>
/// 如此既保留官方 <see cref="IStringLocalizer"/>/<see cref="IStringLocalizerFactory"/> 抽象与请求本地化中间件，
/// 又只让 JSON 负责明确登记的资源类型，符合可组合原则。
/// </remarks>
public sealed class CompositeStringLocalizerFactory : IStringLocalizerFactory
{
    private readonly JsonStringLocalizerFactory _json;
    private readonly IStringLocalizerFactory _fallback;
    private readonly HashSet<Assembly> _jsonAssemblies;

    public CompositeStringLocalizerFactory(
        JsonStringLocalizerFactory json,
        ResourceManagerStringLocalizerFactory fallback,
        IOptions<JsonLocalizationOptions> options)
    {
        _json = json;
        _fallback = fallback;
        _jsonAssemblies = [.. options.Value.ResourceAssemblies];
    }

    /// <summary>
    /// 按资源类型所属程序集路由：已登记 JSON 资源程序集走 JSON，其余委派官方 RESX 工厂。
    /// </summary>
    public IStringLocalizer Create(Type resourceSource)
    {
        ArgumentNullException.ThrowIfNull(resourceSource);
        return _jsonAssemblies.Contains(resourceSource.Assembly)
            ? _json.Create(resourceSource)
            : _fallback.Create(resourceSource);
    }

    /// <summary>
    /// 按 <paramref name="location"/>（程序集名）路由：匹配到已登记 JSON 资源程序集走 JSON，其余委派官方 RESX 工厂。
    /// </summary>
    public IStringLocalizer Create(string baseName, string location)
    {
        return _jsonAssemblies.Any(assembly =>
            string.Equals(assembly.GetName().Name, location, StringComparison.OrdinalIgnoreCase))
            ? _json.Create(baseName, location)
            : _fallback.Create(baseName, location);
    }
}
