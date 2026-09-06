using CompanyName.ProjectName.Infrastructure.Persistence;
using CompanyName.ProjectName.Infrastructure.TenantConnections;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using Leistd.Data.Constants;
#if (OpenIddictServer)
using OpenIddict.EntityFrameworkCore;
#endif

namespace CompanyName.ProjectName.DbMigrator;

/// <summary>
/// 预演或施加所有物理数据库目标的 EF Core 迁移。
/// </summary>
/// <remarks>
/// 默认只输出计划和 SQL；显式施加时由 EF Core 管理迁移锁和事务，不得再包裹外层事务。
/// 日志和报告使用连接串的 SHA256 指纹标识目标。
/// </remarks>
public sealed class DatabaseMigrationRunner(
    IConfiguration configuration,
    ITenantMigrationTargetProvider targetProvider,
    ILogger<DatabaseMigrationRunner> logger)
{
    /// <summary>表示一个物理目标的迁移计划。</summary>
    /// <param name="Target">连接串指纹或 <c>default</c>。</param>
    /// <param name="Scope">同一目标上的模型范围。</param>
    /// <param name="PendingMigrations">待执行迁移；空集合表示已是最新。</param>
    /// <param name="Script">预演生成的 SQL；施加时为空字符串。</param>
    public sealed record MigrationPlan(
        string Target,
        string Scope,
        IReadOnlyList<string> PendingMigrations,
        string Script);

    /// <summary>表示共享同一模型范围和预演 SQL 的目标组。</summary>
    /// <param name="Scope">模型范围。</param>
    /// <param name="Script">该组共用的 SQL。</param>
    /// <param name="Targets">适用的目标标识。</param>
    public sealed record MigrationScriptGroup(
        string Scope,
        string Script,
        IReadOnlyList<string> Targets);

    /// <summary>
    /// 按模型范围和 SQL 合并预演计划。
    /// </summary>
    /// <remarks>
    /// 只有实际执行相同 SQL 的目标才会合并。
    /// </remarks>
    internal static IReadOnlyList<MigrationScriptGroup> GroupScripts(IEnumerable<MigrationPlan> plans)
    {
        ArgumentNullException.ThrowIfNull(plans);

        return plans
            .Where(x => !string.IsNullOrEmpty(x.Script))
            .GroupBy(x => (x.Scope, x.Script))
            .Select(g => new MigrationScriptGroup(
                g.Key.Scope,
                g.Key.Script,
                g.Select(x => x.Target).ToArray()))
            .ToArray();
    }

    /// <summary>表示所有物理目标的完整迁移报告。</summary>
    /// <param name="Plans">各物理目标的计划。</param>
    /// <remarks>
    /// 目标枚举失败时不会返回不完整报告。
    /// </remarks>
    public sealed record MigrationReport(IReadOnlyList<MigrationPlan> Plans);

    /// <summary>
    /// 计算所有物理目标的计划，并按 <paramref name="apply"/> 决定是否施加迁移。
    /// </summary>
    /// <param name="apply"><see langword="false"/> 时只输出清单和 SQL。</param>
    /// <remarks>
    /// 首次安装缺少租户注册表时按无独立目标处理；其他枚举失败均上抛。
    /// </remarks>
    public async Task<MigrationReport> RunAsync(
        bool apply,
        CancellationToken cancellationToken = default)
    {
        var plans = new List<MigrationPlan>();

        var explicitTarget = configuration.GetConnectionString("MigrationTarget");
        if (!string.IsNullOrWhiteSpace(explicitTarget))
        {
            var target = new TenantMigrationTarget(Guid.Empty, explicitTarget);
            plans.Add(await ProcessBusinessAsync(explicitTarget, target.Fingerprint, apply, cancellationToken));
            return new MigrationReport(plans);
        }

        var defaultConnection = configuration.GetConnectionString(ConnectionStringNames.Default);
        if (string.IsNullOrWhiteSpace(defaultConnection))
        {
            throw new InvalidOperationException("ConnectionStrings:Default is required by DbMigrator.");
        }

#if (LocalIdentity)
        // 使用与运行时一致的控制面连接回退链。
        var controlConnection = configuration.GetControlPlaneConnectionString() ?? defaultConnection;

        // 同库沿用 default，独立控制库使用指纹标识。
        var controlTarget = string.Equals(controlConnection, defaultConnection, StringComparison.Ordinal)
            ? "default"
            : new TenantMigrationTarget(Guid.Empty, controlConnection).Fingerprint;
#endif

        // 仅控制库的待执行计划可证明本地租户表尚未创建。
        MigrationPlan? controlPlan = null;
#if (LocalIdentity)
        controlPlan = await ProcessControlAsync(controlConnection, controlTarget, apply, cancellationToken);
        plans.Add(controlPlan);
#endif
#if (OpenIddictServer)
        // OIDC 与控制面同库，但有独立上下文和迁移历史。
        plans.Add(await ProcessOpenIddictAsync(controlConnection, controlTarget, apply, cancellationToken));
#endif
        plans.Add(await ProcessBusinessAsync(defaultConnection, "default", apply, cancellationToken));

        // 始终真实枚举目标；只有已证实的首次安装可将缺表视为无独立目标。
        // 其他失败必须非零退出，防止不完整预演随后施加额外目标。
        IReadOnlyList<TenantMigrationTarget> targets;
        try
        {
            targets = await targetProvider.GetDedicatedTargetsAsync(cancellationToken);
        }
        catch (Exception exception) when (IsFirstInstall(exception, apply, controlPlan))
        {
            logger.LogInformation(
                "The tenant registry does not exist yet and the control database has not been migrated; " +
                "treating this run as a first install with no dedicated targets.");
            return new MigrationReport(plans);
        }

        foreach (var target in targets.DistinctBy(x => x.Fingerprint, StringComparer.Ordinal))
        {
            plans.Add(await ProcessBusinessAsync(target.ConnectionString, target.Fingerprint, apply, cancellationToken));
        }

        return new MigrationReport(plans);
    }

#if (LocalIdentity)
    private async Task<MigrationPlan> ProcessControlAsync(
        string connectionString,
        string target,
        bool apply,
        CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<IdentityControlDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable(
                    DatabaseSchema.ControlMigrationsHistoryTable, DatabaseSchema.Name));

        await using var dbContext = new IdentityControlDbContext(options.Options, serviceProvider: null);
        return await ProcessAsync(dbContext, target, "control", apply, cancellationToken);
    }
#endif

#if (OpenIddictServer)
    private async Task<MigrationPlan> ProcessOpenIddictAsync(
        string connectionString,
        string target,
        bool apply,
        CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<OpenIddictDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable(
                    OpenIddictDbContext.MigrationsHistoryTable, DatabaseSchema.Name))
            .UseOpenIddict();

        await using var dbContext = new OpenIddictDbContext(options.Options);
        return await ProcessAsync(dbContext, target, "openiddict", apply, cancellationToken);
    }
#endif

    private async Task<MigrationPlan> ProcessBusinessAsync(
        string connectionString,
        string targetFingerprint,
        bool apply,
        CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<MyProjectDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable(
                    DatabaseSchema.BusinessMigrationsHistoryTable, DatabaseSchema.Name));

        await using var dbContext = new MyProjectDbContext(options.Options, serviceProvider: null);
        return await ProcessAsync(dbContext, targetFingerprint, "business", apply, cancellationToken);
    }

    // 仅只读预演中未迁移的控制库缺少租户表时，才判定为首次安装。
    private static bool IsFirstInstall(Exception exception, bool apply, MigrationPlan? controlPlan)
    {
        if (apply)
        {
            return false;
        }

        if (controlPlan is null)
        {
            return false;
        }

        if (controlPlan.PendingMigrations.Count == 0)
        {
            return false;
        }

        return IsMissingTenantRegistry(exception);
    }

    private static bool IsMissingTenantRegistry(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: "42P01" })
            {
                return true;
            }
        }

        return false;
    }

    private async Task<MigrationPlan> ProcessAsync(
        DbContext dbContext,
        string target,
        string scope,
        bool apply,
        CancellationToken cancellationToken)
    {
        // 读取待执行迁移不会创建历史表，适合安全预演。
        var pending = (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

        if (pending.Count == 0)
        {
            logger.LogInformation("Target {DatabaseTarget} ({Scope}) is up to date.", target, scope);
            return new MigrationPlan(target, scope, pending, string.Empty);
        }

        if (!apply)
        {
            logger.LogInformation(
                "Target {DatabaseTarget} ({Scope}) has {PendingCount} pending migration(s); no change applied.",
                target, scope, pending.Count);

            // 从最后一个已施加迁移生成脚本；首次迁移从初始状态生成。
            var applied = await dbContext.Database.GetAppliedMigrationsAsync(cancellationToken);
            var script = dbContext.GetService<IMigrator>().GenerateScript(
                fromMigration: applied.LastOrDefault(),
                toMigration: null,
                options: MigrationsSqlGenerationOptions.Default);

            return new MigrationPlan(target, scope, pending, script);
        }

        logger.LogInformation(
            "Applying {PendingCount} migration(s) to target {DatabaseTarget} ({Scope}).",
            pending.Count, target, scope);

        // EF Core 自行管理迁移锁和事务，不能包裹外层事务。
        await dbContext.Database.MigrateAsync(cancellationToken);
        return new MigrationPlan(target, scope, pending, string.Empty);
    }
}
