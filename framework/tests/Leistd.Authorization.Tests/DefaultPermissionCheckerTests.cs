using Xunit;

namespace Leistd.Authorization.Tests;

public class DefaultPermissionCheckerTests
{
    [Fact]
    public async Task Returns_false_when_current_subject_is_null()
    {
        var checker = CreateChecker(null);

        Assert.False(await checker.IsGrantedAsync(TestPermissionDefinitionProvider.OrdersRead));

        var multiple = await checker.IsGrantedAsync(
            [TestPermissionDefinitionProvider.OrdersRead, TestPermissionDefinitionProvider.OrdersWrite]);

        Assert.False(multiple.AnyGranted);
    }

    [Fact]
    public async Task Returns_false_for_undefined_permission_even_for_super_admin()
    {
        var checker = CreateChecker(new PermissionSubject("u1", [], IsSuperAdmin: true));

        // 未定义的权限名默认拒绝：拼错或数据库残留的权限不会因为超管而生效。
        Assert.False(await checker.IsGrantedAsync(TestPermissionDefinitionProvider.Undefined));
        Assert.False(await checker.IsGrantedAsync(""));
        Assert.False(await checker.IsGrantedAsync(" "));
    }

    [Fact]
    public async Task Returns_false_for_disabled_permission_and_its_children()
    {
        var store = new FakePermissionGrantStore();
        store.SetUserGrants("u1",
            new PermissionGrant(TestPermissionDefinitionProvider.Reports, PermissionGrantEffect.Granted),
            new PermissionGrant(TestPermissionDefinitionProvider.ReportsView, PermissionGrantEffect.Granted));

        var checker = CreateChecker(new PermissionSubject("u1", [], IsSuperAdmin: false), store);

        // 即使数据库里存在授予，被禁用的定义一律拒绝。
        Assert.False(await checker.IsGrantedAsync(TestPermissionDefinitionProvider.Reports));
        Assert.False(await checker.IsGrantedAsync(TestPermissionDefinitionProvider.ReportsView));
    }

    [Fact]
    public async Task Super_admin_bypasses_grants_without_touching_the_store()
    {
        var store = new FakePermissionGrantStore();
        var checker = CreateChecker(new PermissionSubject("u1", ["r1"], IsSuperAdmin: true), store);

        Assert.True(await checker.IsGrantedAsync(TestPermissionDefinitionProvider.OrdersRead));
        Assert.True(await checker.IsGrantedAsync(TestPermissionDefinitionProvider.OrdersDelete));
        Assert.Equal(0, store.SubjectQueryCount);
    }

    [Fact]
    public async Task Combines_user_and_role_grants_with_deny_taking_precedence()
    {
        var store = new FakePermissionGrantStore();
        store.SetRoleGrants("r1",
            new PermissionGrant(TestPermissionDefinitionProvider.Orders, PermissionGrantEffect.Granted),
            new PermissionGrant(TestPermissionDefinitionProvider.OrdersRead, PermissionGrantEffect.Granted),
            new PermissionGrant(TestPermissionDefinitionProvider.OrdersDelete, PermissionGrantEffect.Granted));
        store.SetUserGrants("u1",
            new PermissionGrant(TestPermissionDefinitionProvider.OrdersDelete, PermissionGrantEffect.Prohibited));

        var checker = CreateChecker(new PermissionSubject("u1", ["r1"], IsSuperAdmin: false), store);

        Assert.True(await checker.IsGrantedAsync(TestPermissionDefinitionProvider.OrdersRead));
        // 角色授予了删除，但用户上有显式拒绝：拒绝优先。
        Assert.False(await checker.IsGrantedAsync(TestPermissionDefinitionProvider.OrdersDelete));
        // 从未授予：默认拒绝。
        Assert.False(await checker.IsGrantedAsync(TestPermissionDefinitionProvider.OrdersWrite));
    }

    [Fact]
    public async Task Deny_from_a_role_also_beats_grant_from_another_role()
    {
        var store = new FakePermissionGrantStore();
        store.SetRoleGrants("r1",
            new PermissionGrant(TestPermissionDefinitionProvider.OrdersRead, PermissionGrantEffect.Granted));
        store.SetRoleGrants("r2",
            new PermissionGrant(TestPermissionDefinitionProvider.OrdersRead, PermissionGrantEffect.Prohibited));

        var checker = CreateChecker(new PermissionSubject("u1", ["r1", "r2"], IsSuperAdmin: false), store);

        Assert.False(await checker.IsGrantedAsync(TestPermissionDefinitionProvider.OrdersRead));
    }

    [Fact]
    public async Task Resolves_subject_and_grants_once_per_scope()
    {
        var store = new FakePermissionGrantStore();
        store.SetUserGrants("u1",
            new PermissionGrant(TestPermissionDefinitionProvider.OrdersRead, PermissionGrantEffect.Granted));

        var subjectProvider = new CountingSubjectProvider(
            new PermissionSubject("u1", ["r1"], IsSuperAdmin: false));
        var checker = new DefaultPermissionChecker(
            subjectProvider,
            TestPermissionDefinitions.CreateManager(),
            store);

        await checker.IsGrantedAsync(TestPermissionDefinitionProvider.OrdersRead);
        await checker.IsGrantedAsync(TestPermissionDefinitionProvider.OrdersWrite);
        await checker.IsGrantedAsync(
            [TestPermissionDefinitionProvider.OrdersRead, TestPermissionDefinitionProvider.OrdersDelete]);

        // 同一作用域内多次检查只解析一次主体、只读取一次授予。
        Assert.Equal(1, subjectProvider.CallCount);
        Assert.Equal(1, store.SubjectQueryCount);
    }

    [Fact]
    public async Task Batch_check_reports_each_requested_permission()
    {
        var store = new FakePermissionGrantStore();
        store.SetUserGrants("u1",
            new PermissionGrant(TestPermissionDefinitionProvider.OrdersRead, PermissionGrantEffect.Granted));

        var checker = CreateChecker(new PermissionSubject("u1", [], IsSuperAdmin: false), store);

        var multiple = await checker.IsGrantedAsync(
        [
            TestPermissionDefinitionProvider.OrdersRead,
            TestPermissionDefinitionProvider.OrdersWrite,
            TestPermissionDefinitionProvider.OrdersRead,
            TestPermissionDefinitionProvider.Undefined,
            ""
        ]);

        Assert.False(multiple.AllGranted);
        Assert.True(multiple.AnyGranted);
        Assert.Equal(3, multiple.Results.Count);
        Assert.True(multiple.Results[TestPermissionDefinitionProvider.OrdersRead]);
        Assert.False(multiple.Results[TestPermissionDefinitionProvider.OrdersWrite]);
        Assert.False(multiple.Results[TestPermissionDefinitionProvider.Undefined]);
        Assert.False(multiple.Results.ContainsKey(""));
    }

    private static DefaultPermissionChecker CreateChecker(
        PermissionSubject? subject,
        FakePermissionGrantStore? store = null)
    {
        return new DefaultPermissionChecker(
            new CountingSubjectProvider(subject),
            TestPermissionDefinitions.CreateManager(),
            store ?? new FakePermissionGrantStore());
    }

    private sealed class CountingSubjectProvider(PermissionSubject? subject) : IPermissionSubjectProvider
    {
        public int CallCount { get; private set; }

        public Task<PermissionSubject?> GetCurrentSubjectAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(subject);
        }
    }

    private sealed class FakePermissionGrantStore : IPermissionGrantStore
    {
        private readonly Dictionary<string, List<PermissionGrant>> _userGrants = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<PermissionGrant>> _roleGrants = new(StringComparer.Ordinal);

        public int SubjectQueryCount { get; private set; }

        public void SetUserGrants(string userId, params PermissionGrant[] grants)
            => _userGrants[userId] = [.. grants];

        public void SetRoleGrants(string roleId, params PermissionGrant[] grants)
            => _roleGrants[roleId] = [.. grants];

        public Task<PermissionGrantSet> GetGrantsAsync(
            string providerName,
            string providerKey,
            CancellationToken cancellationToken = default)
        {
            var source = providerName == PermissionGrantProviderNames.User ? _userGrants : _roleGrants;
            var grants = source.TryGetValue(providerKey, out var list) ? list : [];
            return Task.FromResult(new PermissionGrantSet(providerName, providerKey, grants, grants.Count));
        }

        public Task<SubjectPermissionGrants> GetGrantsForSubjectAsync(
            string userId,
            IReadOnlyCollection<string> roleIds,
            CancellationToken cancellationToken = default)
        {
            SubjectQueryCount++;

            var userGrants = _userGrants.TryGetValue(userId, out var list) ? list : [];
            var roleSets = roleIds
                .Select(roleId => new PermissionGrantSet(
                    PermissionGrantProviderNames.Role,
                    roleId,
                    _roleGrants.TryGetValue(roleId, out var roleList) ? roleList : [],
                    0))
                .ToList();

            return Task.FromResult(new SubjectPermissionGrants(
                new PermissionGrantSet(PermissionGrantProviderNames.User, userId, userGrants, 0),
                roleSets));
        }
    }
}
