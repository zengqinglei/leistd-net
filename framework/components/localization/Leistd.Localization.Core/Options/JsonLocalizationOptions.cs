using System.Reflection;
using Microsoft.Extensions.Localization;

namespace Leistd.Localization.Options;

/// <summary>
/// Leistd JSON 本地化选项。
/// </summary>
/// <remarks>
/// 资源为随程序集嵌入的 JSON 文件（顶层 <c>culture</c> + <c>texts</c> 两段结构），
/// 按 <see cref="ResourcesPath"/> 下的 <c>{culture}.json</c> 命名；查表时按 culture 回落到 <see cref="DefaultCulture"/>。
/// </remarks>
public sealed class JsonLocalizationOptions
{
    /// <summary>
    /// 承载嵌入 JSON 资源的程序集集合。宿主与各功能包各自登记自身程序集，
    /// 同一键在多个程序集出现时后登记者覆盖前者（便于业务项目覆盖框架默认文案）。
    /// </summary>
    /// <remarks>
    /// 此集合仅决定「从哪些程序集加载 JSON 词条」；不决定 <see cref="IStringLocalizer{T}"/> 的路由。
    /// typed localizer 的 JSON/RESX 分流由 <see cref="JsonResourceTypes"/> 精确到具体类型，
    /// 避免「登记了 JSON 资源的程序集内所有类型都被吞进 JSON」误伤本应走 RESX 的类型。
    /// </remarks>
    public IList<Assembly> ResourceAssemblies { get; } = [];

    /// <summary>
    /// 获取使用 JSON 词条的资源标记类型。
    /// </summary>
    /// <remarks>未登记的强类型本地化器仍使用宿主的默认工厂；集合为空时不接管任何类型。</remarks>
    public ISet<Type> JsonResourceTypes { get; } = new HashSet<Type>();

    /// <summary>
    /// 嵌入资源相对程序集根的逻辑目录，默认 <c>Resources</c>。
    /// 例：程序集 <c>Foo</c> 下 <c>Resources/en.json</c> 的嵌入清单名为 <c>Foo.Resources.en.json</c>。
    /// </summary>
    public string ResourcesPath { get; set; } = "Resources";

    /// <summary>
    /// 默认/回落语言。当前请求 culture 无对应资源或缺某键时回落到此。默认英语 <c>en</c>。
    /// </summary>
    public string DefaultCulture { get; set; } = "en";
}
