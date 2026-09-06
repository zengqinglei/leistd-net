using CompanyName.ProjectName.DbMigrator;
using CompanyName.ProjectName.Domain;
using CompanyName.ProjectName.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// 默认只读预演，--apply 才写库。迁移是不可逆的生产数据变更，
// "跑一下看看"和"改掉生产库"不能是同一个动作。
var apply = false;
foreach (var argument in args)
{
    switch (argument)
    {
        case "--apply":
            apply = true;
            break;

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

// 刻意不把 args 交给宿主：命令行配置提供程序会把 `--apply`（无值开关）当配置键解析。
// 本作业的配置来自环境变量与 appsettings——K8s Job 的标准做法，不需要命令行覆盖。
var builder = Host.CreateApplicationBuilder();
builder.Services.AddDomainServices();
builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddTenantMigrationServices();
builder.Services.AddScoped<DatabaseMigrationRunner>();

try
{
    using var host = builder.Build();
    await using var scope = host.Services.CreateAsyncScope();
    var report = await scope.ServiceProvider
        .GetRequiredService<DatabaseMigrationRunner>()
        .RunAsync(apply);

    WriteReport(report, apply);
    return 0;
}
catch (Exception exception)
{
    // 只写类型名不足以定位：迁移失败时运维需要知道是哪个目标、哪一条迁移。
    // 连接串不会出现在这里——目标以 SHA256 指纹标识，Secret 解析失败的消息也不含明文。
    Console.Error.WriteLine($"Database migration failed: {exception.GetType().Name}: {exception.Message}");
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
}

static void WriteReport(DatabaseMigrationRunner.MigrationReport report, bool apply)
{
    var plans = report.Plans;
    var pendingPlans = plans.Where(x => x.PendingMigrations.Count > 0).ToList();

    Console.WriteLine(apply ? "=== Migrations applied ===" : "=== Dry run: pending migrations ===");

    if (pendingPlans.Count == 0)
    {
        Console.WriteLine($"All {plans.Count} target(s) are up to date. Nothing to do.");
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

    Console.WriteLine("Dry run complete. No change was applied. Re-run with --apply to execute.");
}
