using System.Globalization;
using Leistd.Localization.Core.Json;
using Leistd.Localization.Core.Options;
using Microsoft.Extensions.Options;
using Xunit;

namespace Leistd.Localization.Tests;

public class JsonStringLocalizerTests
{
    private static JsonStringLocalizer CreateLocalizer(string defaultCulture = "en")
    {
        var options = Options.Create(new JsonLocalizationOptions { DefaultCulture = defaultCulture });
        // 使用框架 Core 自带的嵌入资源（en.json / zh-CN.json）
        options.Value.ResourceAssemblies.Add(typeof(JsonLocalizationOptions).Assembly);
        var reader = new JsonLocalizationResourceReader(options);
        return new JsonStringLocalizer(reader, options);
    }

    private static void WithCulture(string culture, Action action)
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
            action();
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public void Resolves_english_by_default()
    {
        var localizer = CreateLocalizer();
        WithCulture("en", () =>
        {
            var value = localizer["Error:NotFound"];
            Assert.False(value.ResourceNotFound);
            Assert.Equal("The requested resource was not found.", value.Value);
        });
    }

    [Fact]
    public void Resolves_chinese_when_current_culture_is_zh_CN()
    {
        var localizer = CreateLocalizer();
        WithCulture("zh-CN", () =>
        {
            var value = localizer["Error:NotFound"];
            Assert.False(value.ResourceNotFound);
            Assert.Equal("请求的资源不存在。", value.Value);
        });
    }

    [Fact]
    public void Falls_back_to_default_culture_when_current_culture_unsupported()
    {
        var localizer = CreateLocalizer(defaultCulture: "en");
        // fr 无资源 → 回落到默认 en
        WithCulture("fr-FR", () =>
        {
            var value = localizer["Error:NotFound"];
            Assert.Equal("The requested resource was not found.", value.Value);
        });
    }

    [Fact]
    public void Falls_back_to_parent_culture()
    {
        var localizer = CreateLocalizer();
        // zh-Hans-CN 无直配 → 逐级回落链最终命中 zh-CN? 注：本资源以 zh-CN 命名，
        // 故用 zh-CN 的子文化验证父链回落到 en 默认（zh-Hans 无资源）。
        WithCulture("zh-Hans", () =>
        {
            var value = localizer["Error:NotFound"];
            // zh-Hans / zh 均无资源 → 回落默认 en
            Assert.Equal("The requested resource was not found.", value.Value);
        });
    }

    [Fact]
    public void Returns_key_itself_when_not_found()
    {
        var localizer = CreateLocalizer();
        WithCulture("en", () =>
        {
            var value = localizer["余额不足"]; // 未启用/未配置的原始中文，当作键
            Assert.True(value.ResourceNotFound);
            Assert.Equal("余额不足", value.Value); // 键即默认值：原样返回
        });
    }

    [Fact]
    public void GetAllStrings_includes_default_keys()
    {
        var localizer = CreateLocalizer();
        WithCulture("en", () =>
        {
            var all = localizer.GetAllStrings(includeParentCultures: true).ToList();
            Assert.Contains(all, s => s.Name == "Error:InternalServer");
            Assert.Contains(all, s => s.Name == "Title:404");
        });
    }
}
