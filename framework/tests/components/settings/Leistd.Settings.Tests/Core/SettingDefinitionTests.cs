using Leistd.Settings.Abstractions;
using Leistd.Settings.Definitions;
using Leistd.Settings.Services;
using Xunit;

namespace Leistd.Settings.Tests.Core;

public class SettingDefinitionTests
{
    [Fact]
    public void Definitions_from_all_providers_are_merged()
    {
        var manager = Build(
            new DelegateProvider(c => c.Add("Display.Language", "zh-CN", SettingScopes.All)),
            new DelegateProvider(c => c.Add("Export.MaxRowsPerFile")));

        Assert.Equal(["Display.Language", "Export.MaxRowsPerFile"], manager.GetAll().Select(d => d.Name).Order());
    }

    // 重名会让后注册的那份定义静默失效，读取时表现为"默认值不对"，所以就地失败。
    // 定义是惰性汇总的，容器不会主动实例化管理器，因此这个失败发生在第一次访问定义时，
    // 而不是宿主启动时——测试也只能这样触发它。
    [Fact]
    public void A_duplicate_name_fails_when_definitions_are_first_accessed()
    {
        var manager = Build(
            new DelegateProvider(c => c.Add("Display.Language")),
            new DelegateProvider(c => c.Add("Display.Language")));

        var error = Assert.Throws<InvalidOperationException>(() => manager.GetAll());

        Assert.Contains("globally unique", error.Message);
    }

    [Fact]
    public void Undefined_names_resolve_to_null()
    {
        var manager = Build(new DelegateProvider(c => c.Add("Display.Language")));

        Assert.Null(manager.GetOrNull("Nope"));
    }

    private static ISettingDefinitionManager Build(params ISettingDefinitionProvider[] providers)
        => new SettingDefinitionManager(providers);

    private sealed class DelegateProvider(Action<ISettingDefinitionContext> define) : ISettingDefinitionProvider
    {
        public void Define(ISettingDefinitionContext context) => define(context);
    }
}
