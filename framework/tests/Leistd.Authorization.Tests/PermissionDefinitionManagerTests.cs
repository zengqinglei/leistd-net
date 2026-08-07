using Xunit;

namespace Leistd.Authorization.Tests;

public class PermissionDefinitionManagerTests
{
    [Fact]
    public void GetGroups_exposes_declared_groups_and_their_top_level_permissions()
    {
        var manager = TestPermissionDefinitions.CreateManager();

        var group = Assert.Single(manager.GetGroups());

        Assert.Equal("App", group.Name);
        Assert.Equal("应用权限", group.DisplayName);
        Assert.Equal(
            [TestPermissionDefinitionProvider.Orders, TestPermissionDefinitionProvider.Reports],
            group.Permissions.Select(x => x.Name));
    }

    [Fact]
    public void GetAncestorNames_returns_the_chain_from_nearest_to_farthest()
    {
        var manager = TestPermissionDefinitions.CreateManager();

        Assert.Equal(
            [TestPermissionDefinitionProvider.OrdersWrite, TestPermissionDefinitionProvider.Orders],
            manager.GetAncestorNames(TestPermissionDefinitionProvider.OrdersWriteBatch));

        Assert.Empty(manager.GetAncestorNames(TestPermissionDefinitionProvider.Orders));
        Assert.Empty(manager.GetAncestorNames(TestPermissionDefinitionProvider.Undefined));
    }

    [Fact]
    public void GetDescendantNames_returns_the_whole_subtree()
    {
        var manager = TestPermissionDefinitions.CreateManager();

        Assert.Equal(
            [
                TestPermissionDefinitionProvider.OrdersRead,
                TestPermissionDefinitionProvider.OrdersWrite,
                TestPermissionDefinitionProvider.OrdersWriteBatch,
                TestPermissionDefinitionProvider.OrdersDelete
            ],
            manager.GetDescendantNames(TestPermissionDefinitionProvider.Orders));

        Assert.Empty(manager.GetDescendantNames(TestPermissionDefinitionProvider.OrdersRead));
    }

    [Fact]
    public void IsEffectivelyEnabled_is_false_for_undefined_disabled_and_descendants_of_disabled()
    {
        var manager = TestPermissionDefinitions.CreateManager();

        Assert.True(manager.IsEffectivelyEnabled(TestPermissionDefinitionProvider.OrdersRead));

        Assert.False(manager.IsEffectivelyEnabled(TestPermissionDefinitionProvider.Undefined));
        Assert.False(manager.IsEffectivelyEnabled(TestPermissionDefinitionProvider.Reports));
        // 父权限被禁用时，子权限同样不可用。
        Assert.False(manager.IsEffectivelyEnabled(TestPermissionDefinitionProvider.ReportsView));
        Assert.False(manager.IsEffectivelyEnabled(""));
    }

    [Fact]
    public void Duplicate_permission_names_fail_fast_at_startup()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => TestPermissionDefinitions.CreateManager(new DuplicatePermissionProvider()));

        Assert.Contains("App.Duplicated", exception.Message);
    }

    private sealed class DuplicatePermissionProvider : IPermissionDefinitionProvider
    {
        public void Define(IPermissionDefinitionContext context)
        {
            context.GetOrAddGroup("A").AddPermission("App.Duplicated");
            context.GetOrAddGroup("B").AddPermission("App.Duplicated");
        }
    }
}
