using Leistd.Settings.Abstractions;
using Leistd.Settings.Definitions;
using Leistd.Settings.Exceptions;
using Leistd.Settings.Services;
using Xunit;

namespace Leistd.Settings.Tests.Core;

// 写入前对照定义校验：未定义的名称与不允许的层级都要在写入点拒绝。
public class SettingWriteTests
{
    [Fact]
    public async Task Writing_an_undefined_setting_fails()
    {
        var (manager, store) = Build();

        await Assert.ThrowsAsync<UndefinedSettingException>(
            () => manager.SetAsync("Nope", "x", SettingScopes.Tenant));

        Assert.Empty(store.Writes);
    }

    // 允许写进未声明的层级，等于让回落顺序失去意义。
    [Fact]
    public async Task Writing_to_a_scope_the_definition_forbids_fails()
    {
        var (manager, store) = Build();

        var error = await Assert.ThrowsAsync<SettingScopeNotAllowedException>(
            () => manager.SetAsync("Export.MaxRowsPerFile", "x", SettingScopes.User, "user-1"));

        Assert.Equal(SettingScopes.Tenant, error.Allowed);
        Assert.Empty(store.Writes);
    }

    [Fact]
    public async Task Writing_a_user_scope_value_requires_a_user_id()
    {
        var (manager, _) = Build();

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => manager.SetAsync("Display.Language", "en-US", SettingScopes.User));
    }

    [Fact]
    public async Task A_tenant_write_never_carries_a_user_id()
    {
        var (manager, store) = Build();

        await manager.SetAsync("Display.Language", "en-US", SettingScopes.Tenant, "user-1");

        Assert.Null(Assert.Single(store.Writes).UserId);
    }

    // null 表示清除该层级的值，使读取回落到下一层。
    [Fact]
    public async Task A_null_value_clears_the_level()
    {
        var (manager, store) = Build();

        await manager.SetAsync("Display.Language", null, SettingScopes.User, "user-1");

        Assert.Null(Assert.Single(store.Writes).Value);
    }

    private static (ISettingManager Manager, FakeSettingStore Store) Build()
    {
        var store = new FakeSettingStore();
        var manager = new DefaultSettingManager(
            new SettingDefinitionManager([new WriteTestDefinitionProvider()]),
            store);
        return (manager, store);
    }

    private sealed class WriteTestDefinitionProvider : ISettingDefinitionProvider
    {
        public void Define(ISettingDefinitionContext context)
        {
            context.Add("Display.Language", "zh-CN", SettingScopes.All);
            context.Add("Export.MaxRowsPerFile", "Acme", SettingScopes.Tenant);
        }
    }
}
