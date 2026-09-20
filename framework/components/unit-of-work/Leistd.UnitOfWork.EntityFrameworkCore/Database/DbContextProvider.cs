using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Leistd.Data;
using Leistd.Data.Connections;

namespace Leistd.UnitOfWork.EntityFrameworkCore.Database;

/// <summary>
/// 在工作单元内按 DbContext 类型复用实例，并在创建前异步解析最终连接串、校验连接归属与物理目标未变；
/// 工作单元之外若作用域内已有实例的连接与本次解析结果不一致，拒绝返回它（不静默改道）。
/// </summary>
public class DbContextProvider<TDbContext>(
    IUnitOfWorkManager unitOfWorkManager,
    IServiceProvider serviceProvider) : IDbContextProvider<TDbContext>
    where TDbContext : DbContext
{
    private static readonly string ConnectionStringName =
        typeof(TDbContext).GetCustomAttribute<ConnectionStringNameAttribute>()?.Name
        ?? ConnectionStringNames.Default;
    private static readonly string DatabaseApiKey = $"EfCoreDbContext:{typeof(TDbContext).FullName}";


    /// <inheritdoc />
    public async Task<TDbContext> GetDbContextAsync(CancellationToken cancellationToken = default)
    {
        var unitOfWork = unitOfWorkManager.Current;
        if (unitOfWork is null)
        {
            // 工作单元之外无需固定连接归属，但仍不许静默改道——理由见 EnsureResolvedTargetIsUsed。
            var resolvedOutsideUnitOfWork = await ResolveConnectionStringAsync(serviceProvider, cancellationToken);
            var scopedDbContext = CreateDbContext(serviceProvider, resolvedOutsideUnitOfWork, existingConnection: null);
            EnsureResolvedTargetIsUsed(scopedDbContext, resolvedOutsideUnitOfWork);
            return scopedDbContext;
        }

        var uowServiceProvider = GetServiceProvider(unitOfWork);
        var binding = uowServiceProvider.GetRequiredService<UnitOfWorkConnectionBinding>();
        // 先校验归属，确保复用已有 DbContext 的快路径同样受约束。
        var affinityKey = uowServiceProvider.GetService<IConnectionAffinityProvider>()?.AffinityKey;
        binding.EnsureAffinity(affinityKey);

        if (unitOfWork.FindDatabaseApi(DatabaseApiKey) is EfCoreDatabaseApi<TDbContext> existingApi)
        {
            return existingApi.DbContext;
        }

        var resolvedConnectionString = await ResolveConnectionStringAsync(uowServiceProvider, cancellationToken);
        string? targetKey;
        if (resolvedConnectionString is not null)
        {
            targetKey = CreateTargetKey(resolvedConnectionString);
        }
        else
        {
            // 没有解析器时先探测宿主配置，避免已绑定连接静默替换 DbContext 的目标。
            targetKey = binding.TargetKey;
            if (targetKey is not null)
            {
                EnsureConfiguredTargetMatches(targetKey);
            }
        }
        var activeTransaction = targetKey is null
            ? null
            : unitOfWork.FindTransactionApi($"EfCoreTransaction:{targetKey}") as EfCoreTransactionApi;
        var dbContext = CreateDbContext(
            uowServiceProvider,
            resolvedConnectionString,
            activeTransaction?.DbContextTransaction.GetDbTransaction().Connection);

        var actualTargetKey = dbContext.Database.IsRelational()
            ? CreateTargetKey(dbContext.Database.GetConnectionString())
            : $"NonRelational:{typeof(TDbContext).FullName}:{dbContext.Database.ProviderName}";
        binding.Bind(affinityKey, actualTargetKey);
        targetKey ??= actualTargetKey;

        if (!string.Equals(targetKey, actualTargetKey, StringComparison.Ordinal))
        {
            // 宿主必须采用解析出的连接，防止写入错误数据库。
            throw new InvalidOperationException(
                $"The DbContext configuration for '{typeof(TDbContext).FullName}' did not use the " +
                $"connection resolved for '{ConnectionStringName}'. The host's AddDbContext callback " +
                "must read DbContextCreationContext.Current.");
        }

        var transactionKey = $"EfCoreTransaction:{targetKey}";
        activeTransaction ??= unitOfWork.FindTransactionApi(transactionKey) as EfCoreTransactionApi;

        ApplyTimeout(dbContext, unitOfWork);

        if (unitOfWork.Options.IsTransactional)
        {
            await EnlistOrBeginTransactionAsync(
                unitOfWork,
                transactionKey,
                dbContext,
                activeTransaction,
                cancellationToken);
        }

        unitOfWork.AddDatabaseApi(DatabaseApiKey, new EfCoreDatabaseApi<TDbContext>(dbContext));
        return dbContext;
    }

    private static async Task EnlistOrBeginTransactionAsync(
        IUnitOfWork unitOfWork,
        string transactionKey,
        TDbContext dbContext,
        EfCoreTransactionApi? activeTransaction,
        CancellationToken cancellationToken)
    {
        if (activeTransaction is null)
        {
            var transaction = unitOfWork.Options.IsolationLevel.HasValue
                ? await dbContext.Database.BeginTransactionAsync(unitOfWork.Options.IsolationLevel.Value, cancellationToken)
                : await dbContext.Database.BeginTransactionAsync(cancellationToken);

            unitOfWork.AddTransactionApi(transactionKey, new EfCoreTransactionApi(transaction, dbContext));
            return;
        }

        if (!dbContext.Database.IsRelational())
        {
            throw new InvalidOperationException(
                "Multiple transactional DbContext types require a relational provider that can share a connection.");
        }

        await dbContext.Database.UseTransactionAsync(
            activeTransaction.DbContextTransaction.GetDbTransaction(),
            cancellationToken);
        activeTransaction.AttendedDbContexts.Add(dbContext);
    }

    // 无解析器时探测宿主配置，并与工作单元绑定的物理目标比较。
    // 独立作用域避免缓存当前作用域的 DbContextOptions；结果按宿主和类型缓存。
    private void EnsureConfiguredTargetMatches(string boundTargetKey)
    {
        // 就地解析 internal 缓存，避免把它暴露在公共构造函数中。
        var configured = serviceProvider
            .GetRequiredService<DbContextConfiguredTargetCache>()
            .GetOrProbe(typeof(TDbContext), ProbeConfiguredTargetKey);

        // 非关系型或无连接串时由实际创建后的绑定继续校验。
        if (configured is null || string.Equals(configured, boundTargetKey, StringComparison.Ordinal))
        {
            return;
        }

        throw new InvalidOperationException(
            $"The DbContext '{typeof(TDbContext).FullName}' is configured for a different physical database " +
            "than the one already bound to this unit of work. A unit of work cannot span two databases: " +
            "two databases mean two transactions, so committing would be able to half-succeed.");
    }

    // 探测宿主为该 DbContext 配置的物理目标。
    private string? ProbeConfiguredTargetKey()
    {
        using var probeScope = serviceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var probe = probeScope.ServiceProvider.GetRequiredService<TDbContext>();

        if (!probe.Database.IsRelational())
        {
            return null;
        }

        var configured = probe.Database.GetConnectionString();
        return configured is null ? null : CreateTargetKey(configured);
    }

    // 工作单元之外取到的 DbContext 由当前 DI 作用域持有（AddDbContext 默认 Scoped）：同一作用域里第一次创建之后，
    // 再取拿到的都是那个实例，宿主回调不会再执行。于是这里解析出的连接串只是"应当用的"，不等于"实际在用的"。
    //
    // 典型事故：请求里先在宿主上下文取过一次（授权阶段读权限授予就会），随后 ICurrentTenant.Change 到分库租户再取，
    // 拿到的仍是宿主库上的实例——计数、查找都在宿主库里执行，得出"该租户没有用户"这类完全不报错的错答案。
    //
    // 另一种做法是工作单元之外一律拒绝取 DbContext。本组件刻意不这么做：宿主侧大量读取本就不需要事务，
    // 一刀切会逼着这些路径都包一层工作单元。这里只拒绝"解析出的连接与实例实际连接不一致"这一种情形，
    // 它正是唯一会静默连错库的情形。判据与工作单元路径同一把尺（CreateTargetKey），不另立等价规则。
    //
    // 修正方式：在目标租户上下文内新开工作单元（BeginAsync(requiresNew: true)）。工作单元自带独立作用域，
    // DbContext 会按解析出的连接重新创建。
    private static void EnsureResolvedTargetIsUsed(TDbContext dbContext, string? resolvedConnectionString)
    {
        // 无解析器（宿主自己配置连接）或非关系型：没有"应当用哪个库"可比。
        if (resolvedConnectionString is null || !dbContext.Database.IsRelational())
        {
            return;
        }

        if (string.Equals(
                CreateTargetKey(resolvedConnectionString),
                CreateTargetKey(dbContext.Database.GetConnectionString()),
                StringComparison.Ordinal))
        {
            return;
        }

        throw new InvalidOperationException(
            $"The DbContext '{typeof(TDbContext).FullName}' held by the current service scope was created for a " +
            $"different database than the one now resolved for '{ConnectionStringName}' (typically after " +
            "ICurrentTenant.Change). Outside a unit of work the scoped instance cannot be re-targeted, so using it " +
            "would silently read and write the wrong database. Begin a new unit of work inside the target tenant " +
            "scope (IUnitOfWorkManager.BeginAsync(requiresNew: true)); it owns its own scope and creates the " +
            "DbContext for the resolved connection.");
    }

    // 连接解析必须在调用此同步方法前完成。
    private static TDbContext CreateDbContext(
        IServiceProvider provider,
        string? connectionString,
        DbConnection? existingConnection)
    {
        if (connectionString is null && existingConnection is null)
        {
            return provider.GetRequiredService<TDbContext>();
        }

        using (DbContextCreationContext.Change(
            connectionString ?? existingConnection!.ConnectionString,
            existingConnection))
        {
            return provider.GetRequiredService<TDbContext>();
        }
    }

    private static async Task<string?> ResolveConnectionStringAsync(
        IServiceProvider provider,
        CancellationToken cancellationToken)
    {
        var resolver = provider.GetService<IConnectionStringResolver>();
        if (resolver is null)
        {
            return null;
        }

        var connectionString = await resolver.ResolveAsync(ConnectionStringName, cancellationToken);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"The connection string resolver returned an empty value for '{ConnectionStringName}'.");
        }

        return connectionString;
    }

    private static string CreateTargetKey(string? connectionString)
    {
        if (connectionString is null)
        {
            return $"ConfiguredDbContext:{typeof(TDbContext).FullName}";
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(connectionString));
        return Convert.ToHexString(bytes);
    }

    private static void ApplyTimeout(TDbContext dbContext, IUnitOfWork unitOfWork)
    {
        if (unitOfWork.Options.Timeout.HasValue &&
            dbContext.Database.IsRelational() &&
            !dbContext.Database.GetCommandTimeout().HasValue)
        {
            dbContext.Database.SetCommandTimeout((int)unitOfWork.Options.Timeout.Value.TotalSeconds);
        }
    }

    // 仅依赖接口公开的服务提供程序，以支持第三方工作单元实现。
    private static IServiceProvider GetServiceProvider(IUnitOfWork unitOfWork)
        => unitOfWork.ServiceProvider;
}
