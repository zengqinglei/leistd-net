using Leistd.Authorization.Definitions;
using Microsoft.Extensions.DependencyInjection;
#if (IncludeLocalization)
using System.Globalization;
using CompanyName.ProjectName.Api;
using Microsoft.Extensions.Localization;
#endif

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>
/// 权限目录的后端约定：每个权限与分组都有可读的默认显示名，常规动作措辞统一，词条与默认文案一致。
/// </summary>
/// <remarks>
/// 权限是授权定义，与前端菜单各自独立演进：菜单引用哪些权限、如何分组，由前端自己的测试约束，这里不读前端源码。
/// </remarks>
public sealed class PermissionCatalogContractTests(ProjectWebApplicationFactory factory)
    : IClassFixture<ProjectWebApplicationFactory>
{
    // 常规动作的统一措辞：英文是定义里的默认文案
    private static readonly Dictionary<string, string> CommonActions = new(StringComparer.Ordinal)
    {
        [".Create"] = "Create",
        [".Update"] = "Edit",
        [".Delete"] = "Delete",
    };

    private IReadOnlyList<IPermissionGroupDefinition> Groups()
        => factory.Services.GetRequiredService<IPermissionDefinitionManager>().GetGroups();

    private static IEnumerable<IPermissionDefinition> Flatten(IPermissionDefinition permission)
        => [permission, .. permission.Children.SelectMany(Flatten)];

    private IEnumerable<IPermissionDefinition> AllPermissions()
        => Groups().SelectMany(group => group.Permissions).SelectMany(Flatten);

    // 不启用本地化时对话框直接展示定义里的文案：它必须是给人看的，而不是技术名或词条键
    [Fact]
    public void Every_permission_and_group_has_a_readable_default_display_name()
    {
        // 分组名本身可以就是可读的英文单词（Audit、System），只要求有文案、不是词条键
        foreach (var group in Groups())
            AssertReadable(group.Name, group.DisplayName);
        // 权限名是 App.Users.Create 这类技术名，显示名不得等于它
        foreach (var permission in AllPermissions())
        {
            AssertReadable(permission.Name, permission.DisplayName);
            Assert.NotEqual(permission.Name, permission.DisplayName);
        }

        static void AssertReadable(string name, string? displayName)
        {
            Assert.False(string.IsNullOrWhiteSpace(displayName), $"{name} 没有默认显示名");
            Assert.DoesNotContain(':', displayName!);
        }
    }

    [Fact]
    public void Common_actions_use_the_shared_wording()
    {
        foreach (var permission in AllPermissions())
        {
            var action = CommonActions.Keys.FirstOrDefault(suffix => permission.Name.EndsWith(suffix, StringComparison.Ordinal));
            if (action is not null)
                Assert.Equal(CommonActions[action], permission.DisplayName);
        }
    }

#if (IncludeLocalization)

    [Theory]
    [InlineData("en")]
    [InlineData("zh-CN")]
    public void Every_permission_and_group_is_translated(string culture)
    {
        var localizer = factory.Services.GetRequiredService<IStringLocalizerFactory>().Create(typeof(ApiResource));
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        try
        {
            string Text(string key)
            {
                var localized = localizer[key];
                Assert.False(localized.ResourceNotFound, $"{culture} 缺少词条 {key}");
                return localized.Value;
            }

            foreach (var permission in AllPermissions())
            {
                var text = Text("Permission:" + permission.Name);
                // 英文资源与定义里的默认文案是同一句话的两份，必须一致，否则改了一处另一处静默盖掉它
                if (culture == "en")
                    Assert.Equal(permission.DisplayName, text);
            }

            foreach (var group in Groups())
            {
                var text = Text("PermissionGroup:" + group.Name);
                if (culture == "en")
                    Assert.Equal(group.DisplayName, text);
            }

            if (culture == "zh-CN")
            {
                var chinese = new Dictionary<string, string> { [".Create"] = "新建", [".Update"] = "编辑", [".Delete"] = "删除" };
                foreach (var permission in AllPermissions())
                {
                    var action = chinese.Keys.FirstOrDefault(suffix => permission.Name.EndsWith(suffix, StringComparison.Ordinal));
                    if (action is not null)
                        Assert.Equal(chinese[action], Text("Permission:" + permission.Name));
                }
            }
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
#endif
}
