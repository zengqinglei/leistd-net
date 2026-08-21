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
            [Environment]::SetEnvironmentVariable($name, $previous[$name])
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
            return
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

function Invoke-Migrator([string]$Assembly, [hashtable]$Environment) {
    Invoke-WithEnvironment $Environment {
        Invoke-External "dotnet" @($Assembly)
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
        @{ Name = "E2E.Identity"; Role = "Identity"; Root = Join-Path $generatedRoot "identity" },
        @{ Name = "E2E.Resource"; Role = "Resource"; Root = Join-Path $generatedRoot "resource" }
    )
    foreach ($project in $projects) {
        Invoke-External "dotnet" @(
            "new", "--debug:custom-hive", $hiveRoot, "fullstack-app",
            "-n", $project.Name, "-o", $project.Root, "--force", "--ServiceRole", $project.Role)
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

    $adminShared = "Host=127.0.0.1;Port=$script:postgresPort;Database=leistd_shared;Username=postgres;Password=$postgresPassword"
    $adminDedicated = "Host=127.0.0.1;Port=$script:postgresPort;Database=leistd_dedicated;Username=postgres;Password=$postgresPassword"
    $identityMigrator = Join-Path $generatedRoot "identity/backend/src/E2E.Identity.DbMigrator/bin/$Configuration/net10.0/E2E.Identity.DbMigrator.dll"
    $resourceMigrator = Join-Path $generatedRoot "resource/backend/src/E2E.Resource.DbMigrator/bin/$Configuration/net10.0/E2E.Resource.DbMigrator.dll"

    Invoke-Migrator $identityMigrator @{ ConnectionStrings__Default = $adminShared }
    Invoke-Migrator $resourceMigrator @{ ConnectionStrings__MigrationTarget = $adminShared }
    Invoke-Migrator $identityMigrator @{ ConnectionStrings__MigrationTarget = $adminDedicated }
    Invoke-Migrator $resourceMigrator @{ ConnectionStrings__MigrationTarget = $adminDedicated }
    # A second pass proves that both initial and explicit-target modes are idempotent.
    Invoke-Migrator $identityMigrator @{ ConnectionStrings__Default = $adminShared }
    Invoke-Migrator $identityMigrator @{ ConnectionStrings__MigrationTarget = $adminDedicated }

    Assert-Equal "1" (Invoke-Postgres "leistd_shared" 'SELECT count(*) FROM "e2e-identity"."__EFMigrationsHistory_Control";') "Identity Control migration is missing."
    Assert-Equal "1" (Invoke-Postgres "leistd_shared" 'SELECT count(*) FROM "e2e-identity"."__EFMigrationsHistory";') "Identity business migration is missing."
    Assert-Equal "1" (Invoke-Postgres "leistd_shared" 'SELECT count(*) FROM "e2e-resource"."__EFMigrationsHistory";') "Resource migration is missing."
    Assert-Equal "1" (Invoke-Postgres "leistd_dedicated" 'SELECT count(*) FROM "e2e-identity"."__EFMigrationsHistory";') "Dedicated Identity migration is missing."
    Assert-Equal "1" (Invoke-Postgres "leistd_dedicated" 'SELECT count(*) FROM "e2e-resource"."__EFMigrationsHistory";') "Dedicated Resource migration is missing."
    Assert-Equal "" (Invoke-Postgres "leistd_dedicated" 'SELECT to_regclass(''"e2e-identity"."TenantRecord"'');') "Identity Control tables leaked into the dedicated business target."

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
    $startInfo.Environment["DefaultAdmin__Password"] = "Admin@123456"
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

    $loginBody = @{ usernameOrEmail = "admin"; password = "Admin@123456" } | ConvertTo-Json
    $login = Invoke-WebRequest -Uri "$baseUrl/api/v1/auth/session-login" -Method Post `
        -ContentType "application/json" -Body $loginBody -SessionVariable apiSession
    Assert-Equal "200" ([string]$login.StatusCode) "Host administrator login failed."

    $sharedBody = @{
        name = "shared-e2e"; displayName = "Shared E2E"; adminEmail = "shared@example.test"
        adminPassword = "Passw0rd!"; databaseMode = "SharedDatabase"
    } | ConvertTo-Json
    $sharedResponse = Invoke-WebRequest -Uri "$baseUrl/api/v1/tenants" -Method Post `
        -ContentType "application/json" -Body $sharedBody -WebSession $apiSession
    $sharedTenant = $sharedResponse.Content | ConvertFrom-Json

    $dedicatedBody = @{
        name = "dedicated-e2e"; displayName = "Dedicated E2E"; adminEmail = "dedicated@example.test"
        adminPassword = "Passw0rd!"; databaseMode = "DedicatedDatabase"
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

    $brokenBody = @{
        name = "broken-e2e"; adminEmail = "broken@example.test"; adminPassword = "Passw0rd!"
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
