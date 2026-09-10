using CompanyName.ProjectName.Application.Settings.Provider;
using Microsoft.Extensions.Configuration;

namespace CompanyName.ProjectName.Application.Settings.Hosting;

/// <summary>
/// 部署配置提供的日志基线
/// </summary>
/// <remarks>
/// 分工是"<b>部署配置给基线，设置表给运行期覆盖</b>"，与注册策略一致：设置项的代码默认值
/// 就是这份基线，清除覆盖值即回落到它，进程内那个开关的初始值也取自它。
/// <para>
/// 三处都读同一份，否则会出现：部署把最小级别配成 <c>Warning</c>、库里没有任何覆盖值，
/// 运行期却按写死的 <c>Information</c> 打日志，而清除覆盖值也只能回到 <c>Information</c>，
/// 永远回不到部署基线。
/// </para>
/// <para>
/// 最小级别读的是 <b>Serilog 自己的配置节</b>，不另立一份键：基线本来就写在那里，
/// 另起一个键就等于同一件事有两个事实源。它有两种合法写法——
/// <c>"MinimumLevel": "Warning"</c> 与 <c>"MinimumLevel": { "Default": "Warning" }</c>，
/// 两种都要认，而且<b>两种同时出现时要按配置源优先级裁决</b>（见 <see cref="From"/>）。
/// 分类级 <c>Override</c> 不在此列：那是"某些命名空间另算"，与全局基线是两件事。
/// </para>
/// </remarks>
/// <param name="MinimumLevel">全局最小级别的基线。</param>
/// <param name="RequestLevel">正常完成的请求记成哪一级的基线。</param>
public sealed record LoggingBaseline(string MinimumLevel, string RequestLevel)
{
    /// <summary>认不出配置值时的兜底级别。</summary>
    public const string FallbackLevel = "Information";

    /// <summary>Serilog 最小级别所在的配置节。</summary>
    public const string MinimumLevelSection = "Serilog:MinimumLevel";

    /// <summary>
    /// 从部署配置读出基线。
    /// </summary>
    /// <remarks>
    /// 请求日志级别没有对应的部署配置键：它不是 Serilog 的概念，而是本项目自己的开关
    /// （"正常完成的请求记成哪一级"），所以基线就是代码默认值。真要让部署配置它，
    /// 在这里加一个键即可，三处消费点不必改。
    /// </remarks>
    /// <param name="configuration">部署配置。</param>
    public static LoggingBaseline From(IConfiguration configuration)
        => new(Normalize(ReadMinimumLevel(configuration)), FallbackLevel);

    /// <summary>
    /// 取最小级别，口径与 Serilog 读同一份配置时完全一致。
    /// </summary>
    /// <remarks>
    /// 两种写法<b>同时存在</b>时必须按配置源优先级裁决，不能固定优先某一种：模板的
    /// <c>appsettings.json</c> 里本来就有对象写法，部署再用环境变量给出标量写法，两个键就都在。
    /// 固定优先对象写法会让低优先级的文件顶掉高优先级的环境变量——而 Serilog 自己用的是后者，
    /// 于是 <c>MinimumLevel.ControlledBy</c> 把它刚解析出来的正确级别又改回去了，
    /// 表现为"环境变量配的日志级别不起作用"。
    /// <para>
    /// 算法照搬 <c>serilog-settings-configuration</c> 的 <c>GetDefaultMinLevelDirective</c>：
    /// 两者都有值时按 provider 倒序找，同一 provider 内标量优先；只有一种写法时标量优先。
    /// </para>
    /// </remarks>
    private static string? ReadMinimumLevel(IConfiguration configuration)
    {
        const string defaultKey = $"{MinimumLevelSection}:Default";
        var scalar = configuration[MinimumLevelSection];
        var nested = configuration[defaultKey];

        if (scalar is not null && nested is not null && configuration is IConfigurationRoot root)
        {
            foreach (var provider in root.Providers.Reverse())
            {
                if (provider.TryGet(MinimumLevelSection, out var fromScalar)
                    && !string.IsNullOrEmpty(fromScalar))
                {
                    return fromScalar;
                }

                if (provider.TryGet(defaultKey, out var fromNested) && !string.IsNullOrEmpty(fromNested))
                {
                    return fromNested;
                }
            }

            return null;
        }

        return scalar ?? nested;
    }

    // 归一到候选项里那份写法：设置值在别处按 Ordinal 比对，配置里写成 "warning" 也要能对上。
    // 认不出的取值退回兜底级别，而不是原样传下去——那会让界面显示一个连自己都不接受的默认值。
    private static string Normalize(string? level) =>
        SettingConstant.Logging.Levels.FirstOrDefault(
            known => string.Equals(known, level, StringComparison.OrdinalIgnoreCase))
        ?? FallbackLevel;
}
