param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$SkipPack
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$runId = "{0}-{1}" -f $PID, (Get-Date -Format "yyyyMMddHHmmssfff")
$runRoot = Join-Path $repoRoot ".tmp/postgresql-e2e/$runId"
$feedRoot = if ($SkipPack) { Join-Path $repoRoot ".tmp/local-feed" } else { Join-Path $runRoot "local-feed" }
$hiveRoot = Join-Path $runRoot "template-hive"
$generatedRoot = Join-Path $runRoot "generated"
$nugetConfigPath = Join-Path $runRoot "NuGet.Config"
$packagesRoot = Join-Path $runRoot "nuget-cache"
$containerName = "leistd-template-e2e-$PID"
$postgresPassword = "pg$([Guid]::NewGuid().ToString('N'))"
$identityRuntimePassword = "id$([Guid]::NewGuid().ToString('N'))"
$resourceRuntimePassword = "rs$([Guid]::NewGuid().ToString('N'))"
$apiProcess = $null
$apiStdoutTask = $null
$apiStderrTask = $null

function Invoke-External([string]$Command, [string[]]$Arguments, [string]$WorkingDirectory = $repoRoot) {
    Push-Location $WorkingDirectory
    try {
        & $Command @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "Command failed with exit code ${LASTEXITCODE}: $Command"
        }
    }
    finally {
        Pop-Location
    }
}

function Get-FreeTcpPort {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    try { return ([Net.IPEndPoint]$listener.LocalEndpoint).Port }
    finally { $listener.Stop() }
}

function Invoke-WithEnvironment([hashtable]$Variables, [scriptblock]$Action) {
    $previous = @{}
    foreach ($name in $Variables.Keys) {
        $previous[$name] = [Environment]::GetEnvironmentVariable($name)
        [Environment]::SetEnvironmentVariable($name, [string]$Variables[$name])
    }

    try { & $Action }
    finally {
        foreach ($name in $Variables.Keys) {
            # A variable that was absent must be REMOVED, not set to "". PowerShell coerces the
            # $null out of a hashtable to an empty string when binding the string parameter, and
            # IConfiguration reports an empty environment variable as "" rather than null -- so a
            # leaked empty ConnectionStrings__* survives ?? fallbacks and reaches the next step as
            # a configured-but-blank connection.
            $value = $previous[$name]
            if ($null -eq $value) {
                [Environment]::SetEnvironmentVariable($name, [NullString]::Value)
            }
            else {
                [Environment]::SetEnvironmentVariable($name, [string]$value)
            }
        }
    }
}

function Invoke-Postgres(
    [string]$Database,
    [string]$Sql,
    [string]$Username = "postgres",
    [string]$Password = $postgresPassword,
    [switch]$ExpectFailure) {
    $previousPassword = $env:PGPASSWORD
    $env:PGPASSWORD = $Password
    try {
        $output = & psql -h 127.0.0.1 -p $script:postgresPort -U $Username -d $Database `
            -v ON_ERROR_STOP=1 -At -c $Sql 2>&1
        $exitCode = $LASTEXITCODE
        if ($ExpectFailure) {
            if ($exitCode -eq 0) { throw "PostgreSQL command unexpectedly succeeded." }
            # 把错误文本还给调用方：只断言"失败了"会让打错的表名、写错的列名一样通过，
            # 调用方需要能核对失败原因正是它预期的那一个
            return ($output -join [Environment]::NewLine).Trim()
        }
        if ($exitCode -ne 0) {
            throw "PostgreSQL command failed: $($output -join [Environment]::NewLine)"
        }
        return ($output -join [Environment]::NewLine).Trim()
    }
    finally {
        $env:PGPASSWORD = $previousPassword
    }
}

function Assert-Equal([string]$Expected, [string]$Actual, [string]$Message) {
    if ($Expected -ne $Actual) {
        throw "$Message Expected '$Expected', actual '$Actual'."
    }
}

function Invoke-Migrator([string]$Assembly, [hashtable]$Environment, [switch]$Apply) {
    $arguments = if ($Apply) { @($Assembly, "--apply") } else { @($Assembly) }
    Invoke-WithEnvironment $Environment {
        Invoke-External "dotnet" $arguments
    }
}

try {
    New-Item -ItemType Directory -Path $runRoot, $hiveRoot, $generatedRoot, $packagesRoot -Force | Out-Null
    if (-not $SkipPack) {
        New-Item -ItemType Directory -Path $feedRoot -Force | Out-Null
        Invoke-External "dotnet" @("pack", "framework/Leistd.Framework.slnx", "-c", $Configuration, "-o", $feedRoot)
    }
    elseif (-not (Test-Path -LiteralPath $feedRoot)) {
        throw "-SkipPack requires packages in $feedRoot."
    }

    $escapedFeed = [Security.SecurityElement]::Escape($feedRoot)
    $escapedPackages = [Security.SecurityElement]::Escape($packagesRoot)
    $nugetConfig = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <config><add key="globalPackagesFolder" value="$escapedPackages" /></config>
  <packageSources>
    <clear />
    <add key="local-feed" value="$escapedFeed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@
    [IO.File]::WriteAllText($nugetConfigPath, $nugetConfig, [Text.UTF8Encoding]::new($false))
    Invoke-External "dotnet" @("new", "--debug:custom-hive", $hiveRoot, "install", (Join-Path $repoRoot "template"), "--force")
    $projects = @(
        # 参数以能力布尔表达：Identity 形态两者皆开，Resource 形态两者皆关。
        # Identity 侧把三个可选特性全开：矩阵的运行时冒烟跑在 EF InMemory 上（不建表），
        # 只有这里会把迁移真的应用到 PostgreSQL，因此模型与迁移是否对齐只能在这条路上验。
        # 曾漏过的实例：ExternalLoginConnections 表不在初始迁移里，开启外部登录的项目
        # 用自己的迁移建不出库（EF 报 PendingModelChangesWarning），而单场景编译一切正常。
        @{ Name = "E2E.Identity"
           Arguments = @("--include-notifications","--include-external-login","--include-localization")
           Root = Join-Path $generatedRoot "identity" },
        @{ Name = "E2E.Resource"
           Arguments = @("--service-role", "Resource")
           Root = Join-Path $generatedRoot "resource" }
    )
    foreach ($project in $projects) {
        # 先拼成一个变量再传：在参数位置直接写 @(...) + $x，PowerShell 会把 + 当成
        # 下一个位置参数的分隔，导致 Invoke-External 的 $Arguments/$WorkingDirectory 错位
        $newArguments = @(
            "new", "--debug:custom-hive", $hiveRoot, "fullstack-app",
            "-n", $project.Name, "-o", $project.Root, "--force") + $project.Arguments
        Invoke-External "dotnet" $newArguments
        $solution = Join-Path $project.Root "backend/$($project.Name).sln"
        Invoke-External "dotnet" @("restore", $solution, "--configfile", $nugetConfigPath, "--force")
        Invoke-External "dotnet" @("build", $solution, "-c", $Configuration, "--no-restore")
    }

    $script:postgresPort = Get-FreeTcpPort
    Invoke-External "docker" @(
        "run", "--detach", "--name", $containerName,
        "-e", "POSTGRES_PASSWORD=$postgresPassword",
        "-p", "${script:postgresPort}:5432", "postgres:15-alpine")

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(60)
    do {
        # The image briefly starts a socket-only bootstrap server before the final TCP server.
        & docker exec $containerName pg_isready -h 127.0.0.1 -U postgres *> $null
        if ($LASTEXITCODE -eq 0) { break }
        Start-Sleep -Milliseconds 500
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    if ($LASTEXITCODE -ne 0) { throw "PostgreSQL container did not become ready." }

    Invoke-External "docker" @("exec", $containerName, "createdb", "-U", "postgres", "leistd_shared")
    Invoke-External "docker" @("exec", $containerName, "createdb", "-U", "postgres", "leistd_dedicated")
    # Two more databases so that "control plane on its own database" can be asserted as a
    # separation, not just as a code path that ran.
    Invoke-External "docker" @("exec", $containerName, "createdb", "-U", "postgres", "leistd_control")
    Invoke-External "docker" @("exec", $containerName, "createdb", "-U", "postgres", "leistd_split_business")

    $adminShared = "Host=127.0.0.1;Port=$script:postgresPort;Database=leistd_shared;Username=postgres;Password=$postgresPassword"
    $adminDedicated = "Host=127.0.0.1;Port=$script:postgresPort;Database=leistd_dedicated;Username=postgres;Password=$postgresPassword"
    $adminControl = "Host=127.0.0.1;Port=$script:postgresPort;Database=leistd_control;Username=postgres;Password=$postgresPassword"
    $adminSplitBusiness = "Host=127.0.0.1;Port=$script:postgresPort;Database=leistd_split_business;Username=postgres;Password=$postgresPassword"
    $identityMigrator = Join-Path $generatedRoot "identity/backend/src/E2E.Identity.DbMigrator/bin/$Configuration/net10.0/E2E.Identity.DbMigrator.dll"
    $resourceMigrator = Join-Path $generatedRoot "resource/backend/src/E2E.Resource.DbMigrator/bin/$Configuration/net10.0/E2E.Resource.DbMigrator.dll"

    # A bare invocation is a dry run: it must report the pending migrations and change nothing.
    # This is the guarantee that matters most for a production migration job, so it is asserted
    # against a real database before anything is applied.
    Invoke-Migrator $identityMigrator @{ ConnectionStrings__Default = $adminShared }
    Assert-Equal "" (Invoke-Postgres "leistd_shared" 'SELECT to_regclass(''"e2e-identity"."__EFMigrationsHistory"'');') "The dry run created schema objects."

    # An unknown argument must fail loudly rather than degrade into a dry run: a typo such as
    # --aply would otherwise leave the operator believing the migration was applied.
    # Assert the exact exit code and message so that "it failed" cannot pass for the wrong reason.
    $typoOutput = Invoke-WithEnvironment @{ ConnectionStrings__Default = $adminShared } {
        & dotnet $identityMigrator "--aply" 2>&1
    }
    Assert-Equal "2" "$LASTEXITCODE" "An unknown DbMigrator argument did not exit with code 2."
    if (($typoOutput -join "`n") -notmatch "Unknown argument: --aply") {
        throw "DbMigrator did not name the unknown argument it rejected."
    }

    Invoke-Migrator $identityMigrator @{ ConnectionStrings__Default = $adminShared } -Apply
    Invoke-Migrator $resourceMigrator @{ ConnectionStrings__MigrationTarget = $adminShared } -Apply
    Invoke-Migrator $identityMigrator @{ ConnectionStrings__MigrationTarget = $adminDedicated } -Apply
    Invoke-Migrator $resourceMigrator @{ ConnectionStrings__MigrationTarget = $adminDedicated } -Apply
    # A second pass proves that both initial and explicit-target modes are idempotent.
    Invoke-Migrator $identityMigrator @{ ConnectionStrings__Default = $adminShared } -Apply
    Invoke-Migrator $identityMigrator @{ ConnectionStrings__MigrationTarget = $adminDedicated } -Apply

    # A dry run against an already-migrated target reports "up to date" and still changes nothing.
    Invoke-Migrator $identityMigrator @{ ConnectionStrings__Default = $adminShared }

    Assert-Equal "1" (Invoke-Postgres "leistd_shared" 'SELECT count(*) FROM "e2e-identity"."__EFMigrationsHistory_Control";') "Identity Control migration is missing."
    Assert-Equal "1" (Invoke-Postgres "leistd_shared" 'SELECT count(*) FROM "e2e-identity"."__EFMigrationsHistory";') "Identity business migration is missing."
    Assert-Equal "1" (Invoke-Postgres "leistd_shared" 'SELECT count(*) FROM "e2e-resource"."__EFMigrationsHistory";') "Resource migration is missing."
    Assert-Equal "1" (Invoke-Postgres "leistd_dedicated" 'SELECT count(*) FROM "e2e-identity"."__EFMigrationsHistory";') "Dedicated Identity migration is missing."
    Assert-Equal "1" (Invoke-Postgres "leistd_dedicated" 'SELECT count(*) FROM "e2e-resource"."__EFMigrationsHistory";') "Dedicated Resource migration is missing."
    Assert-Equal "" (Invoke-Postgres "leistd_dedicated" 'SELECT to_regclass(''"e2e-identity"."TenantRecord"'');') "Identity Control tables leaked into the dedicated business target."

    # Control plane pinned to its own database. DbMigrator must follow the SAME fallback chain as
    # the runtime (IdentityControl ?? Default). Reading only Default would migrate the control
    # schema into the business database while the API reads IdentityControl -- the failure then
    # surfaces as "relation does not exist" at startup, after deployment. The assertions below are
    # a separation in both directions, because "the control tables exist somewhere" would pass
    # even with the bug.
    # The dry run report is the human review gate before --apply, so it must name a DISTINCT
    # physical target for the control plane. Printing "default" for both would let an operator
    # approve a plan while believing the control schema lands in the business database.
    $splitReport = Invoke-WithEnvironment @{
        ConnectionStrings__Default = $adminSplitBusiness
        ConnectionStrings__IdentityControl = $adminControl
    } {
        & dotnet $identityMigrator 2>&1
    }
    $splitReportText = $splitReport -join "`n"
    if ($splitReportText -notmatch '\[control\] target [0-9A-F]{64}') {
        throw "The dry run did not report a distinct physical target for the control plane when IdentityControl was configured separately. Report was:`n$splitReportText"
    }
    if ($splitReportText -notmatch '\[business\] target default') {
        throw "The dry run did not report the business target as 'default'. Report was:`n$splitReportText"
    }

    Invoke-Migrator $identityMigrator @{
        ConnectionStrings__Default = $adminSplitBusiness
        ConnectionStrings__IdentityControl = $adminControl
    } -Apply

    # Existence is checked through pg_tables rather than a count against the table itself: a
    # missing table makes psql fail, and the raw "relation does not exist" then replaces the
    # assertion message that would have explained WHICH separation broke.
    Assert-Equal "1" (Invoke-Postgres "leistd_control" 'SELECT count(*) FROM pg_tables WHERE schemaname = ''e2e-identity'' AND tablename = ''__EFMigrationsHistory_Control'';') "Control migration did not go to the IdentityControl database."
    Assert-Equal "1" (Invoke-Postgres "leistd_control" 'SELECT count(*) FROM pg_tables WHERE schemaname = ''e2e-identity'' AND tablename = ''__EFMigrationsHistory_OpenIddict'';') "OpenIddict migration did not go to the IdentityControl database."
    Assert-Equal "1" (Invoke-Postgres "leistd_split_business" 'SELECT count(*) FROM pg_tables WHERE schemaname = ''e2e-identity'' AND tablename = ''__EFMigrationsHistory'';') "Business migration did not go to the Default database."
    Assert-Equal "1" (Invoke-Postgres "leistd_control" 'SELECT count(*) FROM "e2e-identity"."__EFMigrationsHistory_Control";') "The control migration history in the IdentityControl database is empty."
    Assert-Equal "" (Invoke-Postgres "leistd_split_business" 'SELECT to_regclass(''"e2e-identity"."TenantRecord"'');') "Control tables leaked into the Default database when IdentityControl was configured separately."

    # A migrated control database whose tenant registry is missing is CORRUPTION, not a first
    # install. Treating 42P01 as "first install, no dedicated targets" and exiting 0 would let a
    # deployment believe every target was migrated -- the table could have been dropped, renamed,
    # or created under the wrong schema. The first-install exemption therefore also requires that
    # this is a dry run AND that the control database still had pending migrations.
    Invoke-Postgres "leistd_control" 'DROP TABLE "e2e-identity"."TenantConnectionRecord";' | Out-Null

    $corruptOutput = Invoke-WithEnvironment @{
        ConnectionStrings__Default = $adminSplitBusiness
        ConnectionStrings__IdentityControl = $adminControl
    } {
        & dotnet $identityMigrator "--apply" 2>&1
    }
    $corruptExit = $LASTEXITCODE
    if ($corruptExit -eq 0) {
        throw "DbMigrator exited 0 after --apply although the tenant registry was missing from an already-migrated control database. Output was:`n$($corruptOutput -join "`n")"
    }

    # A dry run against the same corrupt state must also fail: the control database has no pending
    # migrations, so "the table does not exist yet" cannot be true.
    Invoke-WithEnvironment @{
        ConnectionStrings__Default = $adminSplitBusiness
        ConnectionStrings__IdentityControl = $adminControl
    } {
        & dotnet $identityMigrator 2>&1 | Out-Null
    }
    if ($LASTEXITCODE -eq 0) {
        throw "A dry run exited 0 although the tenant registry was missing from an already-migrated control database."
    }

    # No restore: re-running --apply cannot recreate the table, because the migration history still
    # records that migration as applied. It is not needed either -- leistd_control is only used by
    # the assertions above plus the business-history check below, and neither depends on the
    # registry table existing.
    Assert-Equal "" (Invoke-Postgres "leistd_control" 'SELECT to_regclass(''"e2e-identity"."__EFMigrationsHistory"'');') "Business migrations leaked into the IdentityControl database."

    Invoke-Postgres "postgres" "CREATE ROLE e2e_identity_runtime LOGIN PASSWORD '$identityRuntimePassword'; CREATE ROLE e2e_resource_runtime LOGIN PASSWORD '$resourceRuntimePassword';" | Out-Null
    foreach ($database in @("leistd_shared", "leistd_dedicated")) {
        Invoke-Postgres $database @"
GRANT CONNECT ON DATABASE $database TO e2e_identity_runtime, e2e_resource_runtime;
GRANT USAGE ON SCHEMA "e2e-identity" TO e2e_identity_runtime;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA "e2e-identity" TO e2e_identity_runtime;
GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA "e2e-identity" TO e2e_identity_runtime;
GRANT USAGE ON SCHEMA "e2e-resource" TO e2e_resource_runtime;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA "e2e-resource" TO e2e_resource_runtime;
GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA "e2e-resource" TO e2e_resource_runtime;
"@ | Out-Null
    }

    Invoke-Postgres "leistd_shared" 'CREATE TABLE "e2e-identity"."RuntimeMustNotCreateTables"("Id" integer);' `
        -Username "e2e_identity_runtime" -Password $identityRuntimePassword -ExpectFailure

    $identityRuntimeShared = "Host=127.0.0.1;Port=$script:postgresPort;Database=leistd_shared;Username=e2e_identity_runtime;Password=$identityRuntimePassword"
    $identityRuntimeDedicated = "Host=127.0.0.1;Port=$script:postgresPort;Database=leistd_dedicated;Username=e2e_identity_runtime;Password=$identityRuntimePassword"
    $apiPort = Get-FreeTcpPort
    $baseUrl = "http://127.0.0.1:$apiPort"
    $apiAssembly = Join-Path $generatedRoot "identity/backend/src/E2E.Identity.Api/bin/$Configuration/net10.0/E2E.Identity.Api.dll"
    $apiDirectory = Split-Path $apiAssembly
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = (Get-Command dotnet -ErrorAction Stop).Source
    $startInfo.WorkingDirectory = $apiDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.ArgumentList.Add($apiAssembly)
    $startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development"
    $startInfo.Environment["ASPNETCORE_URLS"] = $baseUrl
    $startInfo.Environment["ConnectionStrings__Default"] = $identityRuntimeShared
    $startInfo.Environment["ConnectionStrings__Redis"] = ""
    $startInfo.Environment["SpaProxy__Enabled"] = "false"
    $startInfo.Environment["OAuth__Issuer"] = "$baseUrl/"
    $startInfo.Environment["OAuth__DisableHttpsRequirement"] = "true"
    $startInfo.Environment["DefaultAdmin__Username"] = "admin"
    # 不用模板曾发布过的示例密码：生产校验会拒绝它们，沿用就等于测不到那条校验
    $startInfo.Environment["DefaultAdmin__Password"] = "E2ETests!Adm1n"
    $startInfo.Environment["VerificationCodes__Key"] = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8="
    $startInfo.Environment["TenantSecrets__dedicated-runtime"] = $identityRuntimeDedicated
    $startInfo.Environment["TenantSecrets__dedicated-migration"] = $adminDedicated

    $apiProcess = [Diagnostics.Process]::new()
    $apiProcess.StartInfo = $startInfo
    if (-not $apiProcess.Start()) { throw "Identity API failed to start." }
    $apiStdoutTask = $apiProcess.StandardOutput.ReadToEndAsync()
    $apiStderrTask = $apiProcess.StandardError.ReadToEndAsync()

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(90)
    do {
        if ($apiProcess.HasExited) { throw "Identity API exited with code $($apiProcess.ExitCode)." }
        try {
            $health = Invoke-WebRequest -Uri "$baseUrl/api/health/live" -TimeoutSec 2
            if ($health.StatusCode -eq 200) { break }
        }
        catch { Start-Sleep -Milliseconds 500 }
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    if (-not $health -or $health.StatusCode -ne 200) { throw "Identity API did not become healthy." }

    $loginBody = @{ usernameOrEmail = "admin"; password = "E2ETests!Adm1n" } | ConvertTo-Json
    $login = Invoke-WebRequest -Uri "$baseUrl/api/v1/auth/session-login" -Method Post `
        -ContentType "application/json" -Body $loginBody -SessionVariable apiSession
    Assert-Equal "200" ([string]$login.StatusCode) "Host administrator login failed."

    $sharedBody = @{
        name = "shared-e2e"; displayName = "Shared E2E"; adminEmail = "shared@example.test"
        adminPassword = "E2ETenant!Adm1n"; databaseMode = "SharedDatabase"
    } | ConvertTo-Json
    $sharedResponse = Invoke-WebRequest -Uri "$baseUrl/api/v1/tenants" -Method Post `
        -ContentType "application/json" -Body $sharedBody -WebSession $apiSession
    $sharedTenant = $sharedResponse.Content | ConvertFrom-Json

    $dedicatedBody = @{
        name = "dedicated-e2e"; displayName = "Dedicated E2E"; adminEmail = "dedicated@example.test"
        adminPassword = "E2ETenant!Adm1n"; databaseMode = "DedicatedDatabase"
        runtimeSecretReference = "dedicated-runtime"; migrationSecretReference = "dedicated-migration"
    } | ConvertTo-Json
    $dedicatedResponse = Invoke-WebRequest -Uri "$baseUrl/api/v1/tenants" -Method Post `
        -ContentType "application/json" -Body $dedicatedBody -WebSession $apiSession
    $dedicatedTenant = $dedicatedResponse.Content | ConvertFrom-Json

    $sharedTenantUserSql = 'SELECT count(*) FROM "e2e-identity"."Users" WHERE "TenantId" = ''{0}'' AND NOT "IsDeleted";' -f $sharedTenant.id
    $dedicatedTenantUserSql = 'SELECT count(*) FROM "e2e-identity"."Users" WHERE "TenantId" = ''{0}'' AND NOT "IsDeleted";' -f $dedicatedTenant.id
    $secretReferencesSql = 'SELECT "RuntimeSecretReference" || ''|'' || "MigrationSecretReference" FROM "e2e-identity"."TenantConnectionRecord" WHERE "TenantId" = ''{0}'';' -f $dedicatedTenant.id
    Assert-Equal "1" (Invoke-Postgres "leistd_shared" $sharedTenantUserSql) "Shared tenant data was not stored in the default target."
    Assert-Equal "0" (Invoke-Postgres "leistd_dedicated" $sharedTenantUserSql) "Shared tenant data leaked into the dedicated target."
    Assert-Equal "0" (Invoke-Postgres "leistd_shared" $dedicatedTenantUserSql) "Dedicated tenant data leaked into the default target."
    Assert-Equal "1" (Invoke-Postgres "leistd_dedicated" $dedicatedTenantUserSql) "Dedicated tenant data was not stored in its target."
    Assert-Equal "dedicated-runtime|dedicated-migration" (Invoke-Postgres "leistd_shared" $secretReferencesSql) "Secret references were not stored separately."

    # 超管只属于宿主：CK_User_SuperAdminIsHostOnly。领域服务那道关由集成测试覆盖，但那套跑在
    # 内存提供程序上——检查约束是 PostgreSQL 才生效的 DDL，只有真库能证明它拦得住数据修复脚本、
    # 批量导入和直接 SQL。用刚种出来的真实数据断言，不另造行：
    #   1. 宿主种子确实产出了一个超管（否则下面的 UPDATE 命中 0 行，会假装"约束生效"）；
    #   2. 租户种子产出的管理员没有被标成超管；
    #   3. 把那个超管挪进租户必须被数据库拒绝。
    Assert-Equal "1" (Invoke-Postgres "leistd_shared" 'SELECT count(*) FROM "e2e-identity"."Users" WHERE "IsSuperAdmin" AND "TenantId" IS NULL;') "Host seeding did not produce exactly one super admin."
    Assert-Equal "0" (Invoke-Postgres "leistd_shared" 'SELECT count(*) FROM "e2e-identity"."Users" WHERE "IsSuperAdmin" AND "TenantId" IS NOT NULL;') "Tenant seeding produced a tenant-scoped super admin."
    $constraintViolation = Invoke-Postgres "leistd_shared" ('UPDATE "e2e-identity"."Users" SET "TenantId" = ''{0}'' WHERE "IsSuperAdmin";' -f $sharedTenant.id) -ExpectFailure
    if ($constraintViolation -notmatch "CK_User_SuperAdminIsHostOnly") {
        throw "The super-admin UPDATE failed for a reason other than the check constraint: $constraintViolation"
    }

    $brokenBody = @{
        name = "broken-e2e"; adminEmail = "broken@example.test"; adminPassword = "E2ETenant!Adm1n"
        databaseMode = "DedicatedDatabase"; runtimeSecretReference = "missing-runtime"
        migrationSecretReference = "missing-migration"
    } | ConvertTo-Json
    try {
        Invoke-WebRequest -Uri "$baseUrl/api/v1/tenants" -Method Post -ContentType "application/json" `
            -Body $brokenBody -WebSession $apiSession | Out-Null
        throw "Tenant creation with an unresolved Secret unexpectedly succeeded."
    }
    catch {
        if ($_.Exception.Message -eq "Tenant creation with an unresolved Secret unexpectedly succeeded.") { throw }
    }
    Assert-Equal "0" (Invoke-Postgres "leistd_shared" 'SELECT count(*) FROM "e2e-identity"."TenantRecord" WHERE "NormalizedName" = ''BROKEN-E2E'' AND NOT "IsDeleted";') "Failed tenant provisioning left an active tenant."

    Write-Host "PostgreSQL template E2E passed: package -> generate -> migrate -> API -> shared/dedicated isolation." -ForegroundColor Green
    Write-Host "Artifacts: $runRoot"
}
catch {
    if ($apiProcess -and -not $apiProcess.HasExited) {
        $apiProcess.Kill($true)
        $apiProcess.WaitForExit()
    }
    if ($apiStdoutTask -and $apiStderrTask) {
        $logs = (($apiStdoutTask.GetAwaiter().GetResult()) + [Environment]::NewLine + ($apiStderrTask.GetAwaiter().GetResult())).Trim()
        if ($logs.Length -gt 6000) { $logs = $logs.Substring($logs.Length - 6000) }
        if ($logs) { Write-Error "Identity API log:`n$logs" -ErrorAction Continue }
    }
    throw
}
finally {
    if ($apiProcess) {
        if (-not $apiProcess.HasExited) { $apiProcess.Kill($true) }
        $apiProcess.WaitForExit()
        $apiProcess.Dispose()
    }
    & docker rm --force $containerName *> $null
}
