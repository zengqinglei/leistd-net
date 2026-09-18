using Leistd.MultiTenancy.ConnectionStrings;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leistd.MultiTenancy.Tests.ConnectionResolution;

/// <summary>
/// 运行时的物理库清单：宿主库在前，每个独立库只出现一次，连接串不出现在结果里。
/// </summary>
public class TenantDatabaseEnumeratorTests
{
    private static ITenantDatabaseEnumerator Create(params TenantMigrationConnection[] connections)
    {
        var store = new ScriptedRemoteSource();
        store.Migration.AddRange(connections);
        return new TenantDatabaseEnumerator(new TenantMigrationTargetProvider(store));
    }

    /// <summary>没有租户分库时只剩宿主库：不分库的租户都在里面。</summary>
    [Fact]
    public async Task Without_dedicated_databases_only_the_host_database_is_listed()
    {
        Assert.Equal([TenantDatabase.Host], await Create().GetDatabasesAsync("Default"));
    }

    /// <summary>
    /// 共用一个独立库的租户只列一次，代表取标识最小者
    /// </summary>
    /// <remarks>
    /// 按租户逐个处理会把同一个库跑多遍；而代表必须稳定，否则每次运行的日志对不上。
    /// </remarks>
    [Fact]
    public async Task Tenants_sharing_a_database_are_listed_once_with_a_stable_representative()
    {
        var lower = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var higher = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var alone = Guid.Parse("00000000-0000-0000-0000-000000000003");

        var databases = await Create(
            new TenantMigrationConnection(higher, "Default", "Data Source=shared;Password=s3cret"),
            new TenantMigrationConnection(alone, "Default", "Data Source=alone"),
            new TenantMigrationConnection(lower, "Default", "Data Source=shared;Password=s3cret"))
            .GetDatabasesAsync("Default");

        Assert.Equal([null, lower, alone], databases.Select(database => database.TenantId));
        Assert.All(databases, database => Assert.DoesNotContain("s3cret", database.ToString()));
    }

    /// <summary>
    /// 没有注册任何租户连接解析时，核心注册给出的清单只有宿主库
    /// </summary>
    /// <remarks>
    /// 单库部署与内存库测试都是这种形态。回归点：早先枚举器只随连接解析注册，
    /// 宿主在单库模式下解析不到它，只好自己写一个"只有宿主库"的实现并按模式分支注册。
    /// </remarks>
    [Fact]
    public async Task Without_tenant_connection_resolution_only_the_host_database_is_listed()
    {
        using var provider = new ServiceCollection().AddMultiTenancyCore().BuildServiceProvider();

        var databases = await provider.GetRequiredService<ITenantDatabaseEnumerator>().GetDatabasesAsync("Default");

        Assert.Equal([TenantDatabase.Host], databases);
    }
}
