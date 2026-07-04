using Leistd.Authorization;
using Xunit;

namespace Leistd.Authorization.Tests;

public class DefaultPermissionCheckerTests
{
    [Fact]
    public async Task IsGranted_returns_false_when_current_subject_is_null()
    {
        var checker = new DefaultPermissionChecker(
            new TestSubjectProvider(null),
            new InMemoryPermissionGrantStore());

        Assert.False(await checker.IsGrantedAsync("Orders.Read"));

        var multiple = await checker.IsGrantedAsync(["Orders.Read", "Orders.Write"]);

        Assert.False(multiple.AllGranted);
        Assert.False(multiple.AnyGranted);
        Assert.Equal(
            new Dictionary<string, bool>
            {
                ["Orders.Read"] = false,
                ["Orders.Write"] = false
            },
            multiple.Results);
    }

    [Fact]
    public async Task IsGranted_returns_true_for_super_admin_without_store_grants()
    {
        var checker = new DefaultPermissionChecker(
            new TestSubjectProvider(new PermissionSubject("u1", ["r1"], IsSuperAdmin: true)),
            new InMemoryPermissionGrantStore());

        Assert.True(await checker.IsGrantedAsync("Orders.Read"));

        var multiple = await checker.IsGrantedAsync(["Orders.Read", "Orders.Write", ""]);

        Assert.False(multiple.AllGranted);
        Assert.True(multiple.AnyGranted);
        Assert.True(multiple.Results["Orders.Read"]);
        Assert.True(multiple.Results["Orders.Write"]);
        Assert.False(multiple.Results[""]);
    }

    [Fact]
    public async Task IsGranted_checks_user_and_role_grants_in_batch_for_normal_user()
    {
        var store = new InMemoryPermissionGrantStore();
        store.GrantToUser("Orders.Read", "u1");
        store.GrantToRole("Orders.Write", "r2");
        store.GrantToRole("Orders.Delete", "other-role");

        var checker = new DefaultPermissionChecker(
            new TestSubjectProvider(new PermissionSubject("u1", ["r1", "r2"], IsSuperAdmin: false)),
            store);

        Assert.True(await checker.IsGrantedAsync("Orders.Read"));
        Assert.True(await checker.IsGrantedAsync("Orders.Write"));
        Assert.False(await checker.IsGrantedAsync("Orders.Delete"));

        var multiple = await checker.IsGrantedAsync(
            ["Orders.Read", "Orders.Write", "Orders.Delete", "Orders.Read", ""]);

        Assert.False(multiple.AllGranted);
        Assert.True(multiple.AnyGranted);
        Assert.Equal(4, multiple.Results.Count);
        Assert.True(multiple.Results["Orders.Read"]);
        Assert.True(multiple.Results["Orders.Write"]);
        Assert.False(multiple.Results["Orders.Delete"]);
        Assert.False(multiple.Results[""]);
        Assert.Contains(store.BatchChecks, x =>
            x.UserId == "u1"
            && x.RoleIds.SetEquals(["r1", "r2"])
            && x.PermissionNames.SetEquals(["Orders.Read", "Orders.Write", "Orders.Delete"]));
    }

    [Fact]
    public async Task IsGranted_returns_false_for_empty_permission_names()
    {
        var checker = new DefaultPermissionChecker(
            new TestSubjectProvider(new PermissionSubject("u1", [], IsSuperAdmin: true)),
            new InMemoryPermissionGrantStore());

        Assert.False(await checker.IsGrantedAsync(""));
        Assert.False(await checker.IsGrantedAsync(" "));
    }

    private sealed class TestSubjectProvider(PermissionSubject? subject) : IPermissionSubjectProvider
    {
        public Task<PermissionSubject?> GetCurrentSubjectAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(subject);
        }
    }

    private sealed class InMemoryPermissionGrantStore : IPermissionGrantStore
    {
        private readonly HashSet<(string PermissionName, string UserId)> _userGrants = [];
        private readonly HashSet<(string PermissionName, string RoleId)> _roleGrants = [];

        public List<BatchCheck> BatchChecks { get; } = [];

        public void GrantToUser(string permissionName, string userId)
        {
            _userGrants.Add((permissionName, userId));
        }

        public void GrantToRole(string permissionName, string roleId)
        {
            _roleGrants.Add((permissionName, roleId));
        }

        public Task<bool> IsGrantedToUserAsync(
            string permissionName,
            string userId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_userGrants.Contains((permissionName, userId)));
        }

        public Task<bool> IsGrantedToRoleAsync(
            string permissionName,
            string roleId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_roleGrants.Contains((permissionName, roleId)));
        }

        public Task<bool> IsGrantedToAnyRoleAsync(
            string permissionName,
            IReadOnlyCollection<string> roleIds,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(roleIds.Any(roleId => _roleGrants.Contains((permissionName, roleId))));
        }

        public Task<IReadOnlyDictionary<string, bool>> IsGrantedToUserOrRolesAsync(
            IReadOnlyCollection<string> permissionNames,
            string userId,
            IReadOnlyCollection<string> roleIds,
            CancellationToken cancellationToken = default)
        {
            BatchChecks.Add(new BatchCheck(
                permissionNames.ToHashSet(StringComparer.Ordinal),
                userId,
                roleIds.ToHashSet(StringComparer.Ordinal)));

            var result = permissionNames.ToDictionary(
                x => x,
                x => _userGrants.Contains((x, userId))
                     || roleIds.Any(roleId => _roleGrants.Contains((x, roleId))),
                StringComparer.Ordinal);

            return Task.FromResult<IReadOnlyDictionary<string, bool>>(result);
        }

        public Task<IReadOnlySet<string>> GetGrantedPermissionsForUserAsync(
            string userId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlySet<string>>(
                _userGrants
                    .Where(x => x.UserId == userId)
                    .Select(x => x.PermissionName)
                    .ToHashSet(StringComparer.Ordinal));
        }

        public Task<IReadOnlySet<string>> GetGrantedPermissionsForRoleAsync(
            string roleId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlySet<string>>(
                _roleGrants
                    .Where(x => x.RoleId == roleId)
                    .Select(x => x.PermissionName)
                    .ToHashSet(StringComparer.Ordinal));
        }
    }

    private sealed record BatchCheck(
        HashSet<string> PermissionNames,
        string UserId,
        HashSet<string> RoleIds);
}
