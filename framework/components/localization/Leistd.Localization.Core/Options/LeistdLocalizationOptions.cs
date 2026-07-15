using System.Reflection;

namespace Leistd.Localization.Core.Options;

/// <summary>
/// Leistd JSON 本地化选项。
/// </summary>
/// <remarks>
/// 资源为随程序集嵌入的 JSON 文件（ABP 式 <c>culture</c> + <c>texts</c> 结构），
/// 按 <see cref="ResourcesPath"/> 下的 <c>{culture}.json</c> 命名；查表时按 culture 回落到 <see cref="DefaultCulture"/>。
/// </remarks>
public sealed class LeistdLocalizationOptions
{
    /// <summary>
    /// 承载嵌入 JSON 资源的程序集集合。宿主与各功能包各自登记自身程序集，
    /// 同一键在多个程序集出现时后登记者覆盖前者（便于业务项目覆盖框架默认文案）。
    /// </summary>
    public IList<Assembly> ResourceAssemblies { get; } = [];

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
