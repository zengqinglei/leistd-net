using Leistd.Settings.Abstractions;
using Leistd.Settings.Definitions;
using Leistd.Settings.Exceptions;
using Leistd.Settings.Services;
using Leistd.TestBase.Doubles;
using Xunit;

namespace Leistd.Settings.Tests.Core;

// 回落顺序：用户级 → 租户级 → 代码默认值。
public class SettingResolutionTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task Falls_back_to_the_code_default_when_no_level_has_a_value()
    {
        var provider = Build(new FakeSettingStore());

        Assert.Equal("zh-CN", await provider.GetOrNullAsync("Display.Language"));
    }

    [Fact]
    public async Task A_tenant_value_overrides_the_code_default()
    {
        var store = new FakeSettingStore();
        store.Tenant["Display.Language"] = "en-US";

        Assert.Equal("en-US", await Build(store).GetOrNullAsync("Display.Language"));
    }

    [Fact]
    public async Task A_user_value_overrides_the_tenant_value()
    {
        var store = new FakeSettingStore();
        store.Tenant["Display.Language"] = "en-US";
        store.User["Display.Language"] = "ja-JP";

        Assert.Equal("ja-JP", await Build(store).GetOrNullAsync("Display.Language"));
    }

    // 只允许租户级的设置，即使库里存在用户级的行也不参与回落——
    // 否则改一次定义的 Scopes 就会让历史遗留行悄悄重新生效。
    [Fact]
    public async Task A_user_value_is_ignored_when_the_definition_forbids_the_user_scope()
    {
        var store = new FakeSettingStore();
        store.User["Export.MaxRowsPerFile"] = "leaked";

        Assert.Equal("Acme", await Build(store).GetOrNullAsync("Export.MaxRowsPerFile"));
    }

    // 匿名调用只回落到租户级，不去查用户级。
    [Fact]
    public async Task An_anonymous_caller_resolves_from_the_tenant_level()
    {
        var store = new FakeSettingStore();
        store.Tenant["Display.Language"] = "en-US";
        store.User["Display.Language"] = "ja-JP";

        var provider = Build(store, currentUser: new FakeCurrentUser());

        Assert.Equal("en-US", await provider.GetOrNullAsync("Display.Language"));
        Assert.Equal(0, store.UserReads);
    }

    // 未定义与"值为空"是两件事，不能都表现为返回默认值。
    [Fact]
    public async Task Reading_an_undefined_setting_fails()
    {
        var provider = Build(new FakeSettingStore());

        var error = await Assert.ThrowsAsync<UndefinedSettingException>(() => provider.GetOrNullAsync("Nope"));

        Assert.Equal("Nope", error.SettingName);
    }

    // 一次请求内只查一次库：同一请求里前后两次读取必须看到同一份值。
    [Fact]
    public async Task Values_are_loaded_once_per_scope()
    {
        var store = new FakeSettingStore();
        var provider = Build(store);

        await provider.GetOrNullAsync("Display.Language");
        await provider.GetOrNullAsync("Export.MaxRowsPerFile");
        await provider.GetAllAsync();

        Assert.Equal(1, store.TenantReads);
        Assert.Equal(1, store.UserReads);
    }

    [Theory]
    [InlineData("42", 42)]
    [InlineData("7", 7)]
    public async Task Typed_reads_convert_the_stored_string(string stored, int expected)
    {
        var store = new FakeSettingStore();
        store.Tenant["Display.PageSize"] = stored;

        Assert.Equal(expected, await Build(store).GetAsync<int>("Display.PageSize"));
    }

    [Fact]
    public async Task GetAll_can_be_narrowed_to_client_visible_settings()
    {
        var all = await Build(new FakeSettingStore()).GetAllAsync();
        var visible = await Build(new FakeSettingStore()).GetAllAsync(visibleToClientsOnly: true);

        Assert.Contains("Export.MaxRowsPerFile", all.Keys);
        Assert.DoesNotContain("Export.MaxRowsPerFile", visible.Keys);
        Assert.Contains("Display.Language", visible.Keys);
    }

    // 第一次加载中途失败会在请求级缓存里留下半截状态：租户级已写入、用户级还是 null。
    // 调用方捕获异常后在同一 scope 内重试时，加载会因为「租户级已加载」直接返回，
    // 之后读用户级就是空引用——所以两次查询必须一起发布。
    [Fact]
    public async Task A_failed_load_does_not_poison_the_request_scoped_cache()
    {
        var store = new FakeSettingStore { FailingUserReads = 1 };
        store.Tenant["Display.Language"] = "en-US";
        store.User["Display.Language"] = "ja-JP";
        var provider = Build(store);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetOrNullAsync("Display.Language"));

        // 同一个 Provider 实例重试：这次应当完整加载并解析出用户级的值
        Assert.Equal("ja-JP", await provider.GetOrNullAsync("Display.Language"));
    }

    private static ISettingProvider Build(ISettingStore store, FakeCurrentUser? currentUser = null)
        => new DefaultSettingProvider(
            new SettingDefinitionManager([new TestSettingDefinitionProvider()]),
            store,
            currentUser ?? new FakeCurrentUser(UserId));

    private sealed class TestSettingDefinitionProvider : ISettingDefinitionProvider
    {
        public void Define(ISettingDefinitionContext context)
        {
            context.Add("Display.Language", "zh-CN", SettingScopes.All).IsVisibleToClients = true;
            context.Add("Display.PageSize", "20", SettingScopes.All).IsVisibleToClients = true;
            context.Add("Export.MaxRowsPerFile", "Acme", SettingScopes.Tenant);
        }
    }
}
