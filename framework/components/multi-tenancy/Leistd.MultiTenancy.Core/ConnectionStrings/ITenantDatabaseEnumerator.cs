namespace Leistd.MultiTenancy.ConnectionStrings;

/// <summary>
/// 列出运行时要逐库处理的物理库：宿主库，以及租户登记的每一个独立库。
/// </summary>
/// <remarks>
/// <para><b>为什么需要它。</b>无租户上下文里的 <c>IgnoreQueryFilters()</c> 只能放开"同一个库里的所有租户"，
/// 独立库租户的数据在它自己的库里。归档、清理、扫描这类后台作业若只连宿主库，
/// 独立库永远不会被处理，而且不报错。</para>
/// <para><b>进入某个库的顺序</b>：先切到 <see cref="TenantDatabase.TenantId"/> 的租户上下文，
/// 再新开工作单元（<c>BeginAsync(requiresNew: true)</c>），然后经 <c>IDbContextProvider</c> 取上下文。
/// 工作单元按开启时的租户绑定连接，顺序反过来会被连接归属校验拒绝；
/// 直接注入的 <c>DbContext</c> 不跟随租户路由，拿到的永远是宿主库。</para>
/// <para><b>逐库隔离失败由调用方负责</b>：一个库连不上不该让其余的库也跳过。</para>
/// <para>由 <c>AddMultiTenancyCore()</c> 注册。宿主注册了租户连接解析（本地或远端）时列出独立库；
/// 没有注册时所有租户都在宿主库里，清单只有宿主库——单库部署与内存库测试不必另写实现。</para>
/// <para>停用的租户照常列出：停用限制的是访问，它们库里的数据仍需要维护。
/// 连接串不出现在结果里，只给指纹用于日志。</para>
/// </remarks>
/// <example>
/// <code>
/// foreach (var database in await databaseEnumerator.GetDatabasesAsync(ConnectionStringNames.Default, ct))
/// {
///     try
///     {
///         using (currentTenant.Change(database.TenantId))
///         using (var uow = await unitOfWorkManager.BeginAsync(requiresNew: true))
///         {
///             var dbContext = await dbContextProvider.GetDbContextAsync(ct);
///             // 在这个库里做事；同库的其他租户由 IgnoreQueryFilters() 一并覆盖
///             await uow.CompleteAsync(ct);
///         }
///     }
///     catch (Exception ex) when (ex is not OperationCanceledException)
///     {
///         logger.LogError(ex, "Maintenance failed for database {Database}", database);
///     }
/// }
/// </code>
/// </example>
public interface ITenantDatabaseEnumerator
{
    /// <summary>
    /// 列出指定连接名下的物理库，宿主库在第一个。
    /// </summary>
    /// <remarks>
    /// 独立库清单与迁移作业同一口径（<see cref="ITenantMigrationTargetProvider"/>，去重与代表租户的规则在那里）。
    /// 租户登记的连接恰好指向宿主库时会与宿主库各列一次——逐库逻辑应当可以重复执行。
    /// </remarks>
    /// <param name="name">连接名，通常是业务 DbContext 的 <c>[ConnectionStringName]</c>；大小写不敏感</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<IReadOnlyList<TenantDatabase>> GetDatabasesAsync(
        string name,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 一个物理库，以及进入它要切换到的租户上下文。
/// </summary>
/// <param name="TenantId">
/// 进入该库要切换到的租户；<see langword="null"/> 表示宿主库（也承载所有不分库的租户）。
/// 多个租户共用一个独立库时，这是其中一个代表，库里其余租户的数据要靠 <c>IgnoreQueryFilters()</c> 覆盖。
/// </param>
/// <param name="Fingerprint">连接串的 SHA-256 指纹（十六进制），用于日志；宿主库为 <c>host</c>。</param>
public sealed record TenantDatabase(Guid? TenantId, string Fingerprint)
{
    /// <summary>宿主库。</summary>
    public static TenantDatabase Host { get; } = new(null, "host");
}
