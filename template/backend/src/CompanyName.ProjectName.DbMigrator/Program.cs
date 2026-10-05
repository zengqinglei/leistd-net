using CompanyName.ProjectName.DbMigrator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// 默认只读预演，--apply 才写库。迁移是不可逆的生产数据变更，
// "跑一下看看"和"改掉生产库"不能是同一个动作。
var apply = false;
#if (RemoteTokenAuth)
Guid? adminSubject = null;
Guid? adminTenant = null;
#endif
for (var index = 0; index < args.Length; index++)
{
    var argument = args[index];
    switch (argument)
    {
        case "--apply":
            apply = true;
            break;

#if (RemoteTokenAuth)
        case "--grant-admin":
        case "--tenant":
            if (index + 1 >= args.Length || !Guid.TryParse(args[++index], out var id) || id == Guid.Empty)
            {
                Console.Error.WriteLine($"{argument} requires a non-empty GUID.");
                return 2;
            }
            if (argument == "--grant-admin")
            {
                if (adminSubject.HasValue) { Console.Error.WriteLine("Duplicate --grant-admin."); return 2; }
                adminSubject = id;
            }
            else
            {
#if (IncludeMultiTenancy)
                if (adminTenant.HasValue) { Console.Error.WriteLine("Duplicate --tenant."); return 2; }
                adminTenant = id;
#else
                Console.Error.WriteLine("--tenant is unavailable in a host-only application.");
                return 2;
#endif
            }
            break;
#endif
        case "--help" or "-h":
            WriteUsage();
            return 0;

        default:
            // 不静默忽略：--aply 这类手误若被当成预演，本意是施加的人会以为已经施加了
            Console.Error.WriteLine($"Unknown argument: {argument}");
            WriteUsage();
            return 2;
    }
}

#if (RemoteTokenAuth)
if (adminTenant.HasValue && !adminSubject.HasValue)
{
    Console.Error.WriteLine("--tenant requires --grant-admin.");
    return 2;
}
#endif
// 刻意不把 args 交给宿主：命令行配置提供程序会把 `--apply`（无值开关）当配置键解析。
// 本作业的配置来自环境变量与 appsettings——K8s Job 的标准做法，不需要命令行覆盖。
var builder = Host.CreateApplicationBuilder();
#if (RemoteTokenAuth)
if (adminSubject.HasValue)
{
    builder.ConfigureContainer(new Leistd.DependencyInjection.DynamicProxy.Registration.DynamicProxyServiceRegistrationCallbackFactory());
    builder.Services.AddResourceAdminBootstrapServices(builder.Configuration);
}
else
#endif
{
    builder.Services.AddMigratorServices(builder.Configuration, builder.Environment);
}

try
{
    using var host = builder.Build();
    await using var scope = host.Services.CreateAsyncScope();
#if (RemoteTokenAuth)
    if (adminSubject is { } subject)
    {
        // 启动期闸门（包括必需日志级别）必须在写入前成立。
        await host.StartAsync();
        var result = await scope.ServiceProvider.GetRequiredService<ResourceAdminBootstrapRunner>()
            .RunAsync(subject, adminTenant, apply);
        Console.WriteLine(result);
        await host.StopAsync();
        return 0;
    }
#endif
    var report = await scope.ServiceProvider
        .GetRequiredService<DatabaseMigrationRunner>()
        .RunAsync(apply);

    WriteReport(report, apply);
#if (IncludeMultiTenancy)
    return report.Succeeded ? 0 : 1;
#else
    return 0;
#endif
}
catch (Exception exception)
{
    // 只写类型名不足以定位：迁移失败时运维需要知道是哪个目标、哪一条迁移。
    // 连接串不会出现在这里——目标以 SHA256 指纹标识，解密失败的消息也不含明文。
    Console.Error.WriteLine($"Database command failed: {exception.GetType().Name}: {exception.Message}");
    for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
    {
        Console.Error.WriteLine($"  caused by {inner.GetType().Name}: {inner.Message}");
    }

    return 1;
}

static void WriteUsage()
{
    Console.WriteLine("Usage: dotnet CompanyName.ProjectName.DbMigrator.dll [--apply]");
    Console.WriteLine();
    Console.WriteLine("  (no argument)  Dry run: report pending migrations and the SQL, change nothing.");
    Console.WriteLine("  --apply        Apply the pending migrations.");
#if (RemoteTokenAuth)
#if (IncludeMultiTenancy)
    Console.WriteLine("  --grant-admin <sub> [--tenant <id>]  Preview first administrator grants; add --apply to persist.");
#else
    Console.WriteLine("  --grant-admin <sub>  Preview first administrator grants; add --apply to persist.");
#endif
#endif
}

static void WriteReport(DatabaseMigrationRunner.MigrationReport report, bool apply)
{
    var plans = report.Plans;
    var pendingPlans = plans.Where(x => x.PendingMigrations.Count > 0).ToList();

    Console.WriteLine(apply ? "=== Migrations applied ===" : "=== Dry run: pending migrations ===");
#if (IncludeMultiTenancy)
    // 失败先报：它们决定退出码，而且不能被后面"已是最新"之类的结论盖住
    WriteFailures(report);
#endif

    if (pendingPlans.Count == 0)
    {
#if (IncludeMultiTenancy)
        Console.WriteLine(report.Succeeded
            ? $"All {plans.Count} target(s) are up to date. Nothing to do."
            : $"The {plans.Count} reachable target(s) are up to date; the failures above did not complete.");
#else
        Console.WriteLine($"All {plans.Count} target(s) are up to date. Nothing to do.");
#endif
        return;
    }

    foreach (var plan in pendingPlans)
    {
        Console.WriteLine($"[{plan.Scope}] target {plan.Target}: {plan.PendingMigrations.Count} migration(s)");
        foreach (var migration in plan.PendingMigrations)
        {
            Console.WriteLine($"    {migration}");
        }
    }

    if (apply)
    {
        return;
    }

    // SQL 按「scope + 脚本内容」去重输出，并列出每组适用的目标——
    // 分组键为什么必须含脚本本身，见 DatabaseMigrationRunner.GroupScripts
    Console.WriteLine();
    foreach (var group in DatabaseMigrationRunner.GroupScripts(pendingPlans))
    {
        Console.WriteLine($"--- SQL for scope '{group.Scope}' ({group.Targets.Count} target(s)) ---");
        foreach (var target in group.Targets)
        {
            Console.WriteLine($"    applies to target {target}");
        }

        Console.WriteLine(group.Script);
    }

#if (IncludeMultiTenancy)
    Console.WriteLine(report.Succeeded
        ? "Dry run complete. No change was applied. Re-run with --apply to execute."
        : "Dry run complete. No change was applied. Fix the failures above before re-running with --apply.");
#else
    Console.WriteLine("Dry run complete. No change was applied. Re-run with --apply to execute.");
#endif
}
#if (IncludeMultiTenancy)

// 其余库已经迁移（或预演）完；这些没有完成。失败的库可能已提交了前几条迁移（EF 按迁移各自提交），
// 先看原因与它的迁移历史，修好之后重跑本作业——已施加的迁移不会重复执行
static void WriteFailures(DatabaseMigrationRunner.MigrationReport report)
{
    if (report.Succeeded)
    {
        return;
    }

    foreach (var tenant in report.UnresolvedTenants)
    {
        Console.Error.WriteLine($"Tenant {tenant.TenantId} has no migration target: {tenant.Reason}");
    }

    foreach (var target in report.FailedTargets)
    {
        Console.Error.WriteLine($"Target {target.Target} (tenant {target.TenantId}) did not complete: {target.Reason}");
    }

    Console.Error.WriteLine(
        $"{report.UnresolvedTenants.Count} tenant(s) without a target were not migrated, and {report.FailedTargets.Count} target(s) " +
        "did not complete (they may hold some of the pending migrations). Fix them and run the migrator again.");
}
#endif
