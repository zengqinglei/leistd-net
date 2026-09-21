namespace Leistd.MultiTenancy.ConnectionStrings;

/// <summary>
/// 某个连接名下有哪些独立的物理库，以及每个库住着哪些租户。
/// </summary>
/// <remarks>
/// <para><b>不下发连接串。</b>运行时逐库作业只需要"有哪些库、用哪个租户进得去"——真正的连接由该租户的
/// 正常解析链取得。迁移场景另走 <see cref="ITenantMigrationTargetProvider"/>：那是 DDL 身份、要明文连接串，
/// 两者的权限不是一回事，不能共用一条路径。</para>
/// <para>本地形态直接读控制库；远端形态回源控制面的机器端点，只要"读路由"这一档权限。</para>
/// </remarks>
public interface ITenantDatabaseDirectory
{
    /// <summary>列出该连接名下的独立库；<paramref name="activeOnly"/> 决定停用租户的库算不算。</summary>
    /// <param name="name">连接名（使用方 DbContext 的 <c>[ConnectionStringName]</c>）。</param>
    /// <param name="activeOnly">只列启用租户的库。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<TenantDatabaseListResult> GetDatabasesAsync(
        string name,
        bool activeOnly,
        CancellationToken cancellationToken = default);
}

/// <summary>独立库清单与本轮解析不出来的租户。</summary>
/// <param name="Databases">解析成功的独立库。</param>
/// <param name="FailedTenants">解析失败的租户：坏掉一个不该让整轮作业不执行。</param>
/// <remarks>
/// <b>不回"住在宿主库里的租户"。</b>那份清单的大小等于共享库的租户数，可能上万，
/// 而它要跨 HTTP 边界（远端目录每次逐库作业都拉一遍）。逐库作业也不需要它：
/// 进宿主库用的是宿主配置，不需要某个租户；框架自带的归档与保留期作业用
/// <c>IgnoreQueryFilters()</c> 整库处理，同库里住着谁都一视同仁。
/// 要查"某个租户在哪个库"，直接查连接登记表。
/// </remarks>
public sealed record TenantDatabaseListResult(
    IReadOnlyList<TenantDatabaseEntry> Databases,
    IReadOnlyList<TenantDatabaseFailure> FailedTenants)
{
    /// <summary>没有任何独立库。</summary>
    public static TenantDatabaseListResult Empty { get; } = new([], []);
}

/// <summary>一个独立库，以及住在里面的租户。</summary>
/// <param name="Fingerprint">连接串的指纹，用于判定"是不是同一个库"；不可逆推连接串。</param>
/// <param name="TenantIds">住在这个库里的全部租户，升序。</param>
public sealed record TenantDatabaseEntry(string Fingerprint, IReadOnlyList<Guid> TenantIds);

/// <summary>一个解析不出连接的租户。</summary>
/// <param name="TenantId">租户。</param>
/// <param name="Reason">诊断消息；不含连接串。</param>
public sealed record TenantDatabaseFailure(Guid TenantId, string Reason);
