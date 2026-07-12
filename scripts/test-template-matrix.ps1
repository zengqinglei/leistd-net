param(
    [string[]]$Scenarios = @("default", "minimal", "no-roles", "notifications", "no-openiddict", "external-login"),
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$SkipPack,
    [switch]$SkipFrontend,
    [switch]$SkipRuntime,
    [switch]$ReusePackages
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$tempRoot = Join-Path $repoRoot ".tmp"
$feedRoot = Join-Path $tempRoot "local-feed"
$generatedRoot = Join-Path $tempRoot "generated-template"
$hiveRoot = Join-Path $tempRoot "template-hive"
$packagesRoot = Join-Path $tempRoot "nuget-packages"
$nugetConfigPath = Join-Path $tempRoot "template-matrix.NuGet.Config"
$templateRoot = Join-Path $repoRoot "template"
$nugetOrg = "https://api.nuget.org/v3/index.json"

function Assert-TempPath([string]$Path) {
    $resolvedTemp = [IO.Path]::GetFullPath($tempRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $resolvedPath = [IO.Path]::GetFullPath($Path)
    if (-not $resolvedPath.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify path outside .tmp: $resolvedPath"
    }
}

function Reset-Directory([string]$Path) {
    Assert-TempPath $Path
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
    New-Item -ItemType Directory -Path $Path | Out-Null
}

function Invoke-External([string]$Command, [string[]]$Arguments, [string]$WorkingDirectory = $repoRoot) {
    Write-Host "> $Command $($Arguments -join ' ')" -ForegroundColor DarkGray
    Push-Location $WorkingDirectory
    try {
        & $Command @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "Command failed with exit code ${LASTEXITCODE}: $Command $($Arguments -join ' ')"
        }
    }
    finally {
        Pop-Location
    }
}

function Get-ScenarioProjectName([string]$Scenario) {
    $suffix = (($Scenario -split '-') | ForEach-Object {
        if ($_.Length -eq 0) { return }
        $_.Substring(0, 1).ToUpperInvariant() + $_.Substring(1)
    }) -join ''
    return "Matrix.$suffix"
}

function Assert-MarkdownLinks([string]$ProjectRoot) {
    $brokenLinks = [System.Collections.Generic.List[string]]::new()
    foreach ($file in Get-ChildItem -LiteralPath $ProjectRoot -Recurse -File -Filter "*.md") {
        $lineNumber = 0
        foreach ($line in Get-Content -LiteralPath $file.FullName -Encoding UTF8) {
            $lineNumber++
            foreach ($match in [regex]::Matches($line, '!?\[[^\]]*\]\((?<target>[^)]+)\)')) {
                $rawTarget = $match.Groups['target'].Value.Trim()
                if ($rawTarget -match '^(?:https?://|mailto:|tel:|#)') {
                    continue
                }

                $pathPart = ($rawTarget -split '#', 2)[0].Trim()
                if ($pathPart.StartsWith('<') -and $pathPart.EndsWith('>')) {
                    $pathPart = $pathPart.Trim('<', '>')
                }
                else {
                    $pathPart = ($pathPart -split '\s+', 2)[0]
                }
                $pathPart = ($pathPart -split '\?', 2)[0]
                if ([string]::IsNullOrWhiteSpace($pathPart) -or $pathPart -match '[{}*]') {
                    continue
                }

                $pathPart = [Uri]::UnescapeDataString($pathPart)
                $targetPath = if ($pathPart.StartsWith('/')) {
                    Join-Path $ProjectRoot $pathPart.TrimStart('/')
                }
                else {
                    Join-Path $file.DirectoryName $pathPart
                }
                $targetPath = [IO.Path]::GetFullPath($targetPath)
                if (-not (Test-Path -LiteralPath $targetPath)) {
                    $relativeFile = [IO.Path]::GetRelativePath($ProjectRoot, $file.FullName)
                    $brokenLinks.Add("${relativeFile}:${lineNumber} -> $rawTarget")
                }
            }
        }
    }

    if ($brokenLinks.Count -gt 0) {
        throw "Generated project contains broken Markdown links:`n$($brokenLinks -join "`n")"
    }
}

function Assert-GeneratedProject([string]$ProjectRoot) {
    $requiredFiles = @(
        ".agents/skills/leistd-project-workflow/SKILL.md",
        ".agents/skills/leistd-project-workflow/references/bootstrap.md",
        ".agents/skills/leistd-project-workflow/references/development.md",
        ".agents/skills/leistd-project-workflow/references/quality.md",
        ".agents/skills/leistd-project-workflow/references/delivery.md",
        ".agents/skills/leistd-project-workflow/references/documentation.md",
        "docs/README.md",
        "docs/standards/api.md",
        "docs/standards/coding-common.md",
        "docs/standards/coding-backend.md",
        "docs/standards/coding-frontend.md",
        "docs/standards/project-structure.md",
        "docs/standards/tech-stack.md",
        "docs/standards/testing.md",
        "docs/standards/ui-design.md",
        "docs/deploy/README.md",
        "backend/README.md"
    )
    foreach ($relativePath in $requiredFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $ProjectRoot $relativePath))) {
            throw "Generated project is missing required guidance: $relativePath"
        }
    }

    $skillRoot = Join-Path $ProjectRoot ".agents/skills"
    $skillNames = @(Get-ChildItem -LiteralPath $skillRoot -Directory | ForEach-Object Name)
    if ($skillNames.Count -ne 1 -or $skillNames[0] -ne "leistd-project-workflow") {
        throw "Generated project must contain only leistd-project-workflow in $skillRoot"
    }

    $projectReadme = Get-Content -LiteralPath (Join-Path $ProjectRoot "README.md") -Raw -Encoding UTF8
    foreach ($marker in @("npx skills add ./.agents/skills/leistd-project-workflow", "--agent claude-code", "--copy", "skills-lock.json")) {
        if (-not $projectReadme.Contains($marker)) {
            throw "Generated project README is missing AI CLI compatibility guidance: $marker"
        }
    }

    $expectedStandards = @("api.md", "coding-backend.md", "coding-common.md", "coding-frontend.md", "project-structure.md", "tech-stack.md", "testing.md", "ui-design.md")
    $standardsRoot = Join-Path $ProjectRoot "docs/standards"
    $actualStandards = @(Get-ChildItem -LiteralPath $standardsRoot -File -Filter "*.md" | ForEach-Object Name | Sort-Object)
    $standardDifference = Compare-Object ($expectedStandards | Sort-Object) $actualStandards
    if ($standardDifference -or @(Get-ChildItem -LiteralPath $standardsRoot -Directory).Count -gt 0) {
        throw "Generated project standards must be flat and contain only the expected files: $($expectedStandards -join ', ')"
    }

    $forbiddenPaths = @(
        ".claude",
        "CLAUDE.md",
        "AGENTS.md",
        "backend/CLAUDE.md",
        "backend/AGENTS.md",
        "docs/guides",
        "docs/standards/README.md",
        "docs/standards/agent-workflow.md",
        "docs/standards/api-standard.md",
        "docs/standards/code-standard",
        "docs/standards/test.md",
        "docs/standards/ui-design-strategy.md"
    )
    foreach ($relativePath in $forbiddenPaths) {
        if (Test-Path -LiteralPath (Join-Path $ProjectRoot $relativePath)) {
            throw "Generated project contains removed guidance path: $relativePath"
        }
    }

    $textFiles = Get-ChildItem -LiteralPath $ProjectRoot -Recurse -File | Where-Object {
        $_.FullName -notmatch '[\\/](bin|obj|node_modules|\.git)[\\/]' -and
        ($_.Extension -in @(".cs", ".csproj", ".json", ".md", ".ts", ".html", ".scss", ".yml", ".yaml", ".props", ".targets", ".sln") -or
         $_.Name -in @("Dockerfile", ".gitignore", ".editorconfig"))
    }
    $residuals = $textFiles | Select-String -Pattern '<!--#(if|endif)', 'CompanyName\.ProjectName', '\bMyProject\b', 'companyname-projectname'
    if ($residuals) {
        $sample = $residuals | Select-Object -First 20 | ForEach-Object { "$($_.Path):$($_.LineNumber): $($_.Line.Trim())" }
        throw "Generated project contains template residue:`n$($sample -join "`n")"
    }

    $removedGuidanceReferences = $textFiles | Select-String -Pattern 'template/\.claude', 'agent-workflow\.md', 'api-standard\.md', 'code-standard/(common|backend|frontend)-develop\.md', 'ui-design-strategy\.md'
    if ($removedGuidanceReferences) {
        $sample = $removedGuidanceReferences | Select-Object -First 20 | ForEach-Object { "$($_.Path):$($_.LineNumber): $($_.Line.Trim())" }
        throw "Generated project references removed guidance:`n$($sample -join "`n")"
    }

    $applicationProject = Get-ChildItem -LiteralPath (Join-Path $ProjectRoot "backend/src") -Filter "*.Application.csproj" -Recurse | Select-Object -First 1
    $apiProject = Get-ChildItem -LiteralPath (Join-Path $ProjectRoot "backend/src") -Filter "*.Api.csproj" -Recurse | Select-Object -First 1
    if ((Get-Content -LiteralPath $applicationProject.FullName -Raw).Contains("Infrastructure.csproj")) {
        throw "Application must not reference Infrastructure: $($applicationProject.FullName)"
    }
    if (-not (Get-Content -LiteralPath $apiProject.FullName -Raw).Contains("Infrastructure.csproj")) {
        throw "Api composition root must reference Infrastructure: $($apiProject.FullName)"
    }

    $programText = Get-Content -LiteralPath (Join-Path $apiProject.DirectoryName "Program.cs") -Raw
    if (-not $programText.Contains("await db.Database.MigrateAsync()") -or
        -not $programText.Contains("await db.Database.EnsureCreatedAsync()")) {
        throw "Generated API must initialize relational databases automatically at startup."
    }

    $backendReadme = Get-Content -LiteralPath (Join-Path $ProjectRoot "backend/README.md") -Raw -Encoding UTF8
    if (-not $backendReadme.Contains('无需另行执行 `dotnet ef database update`') -or
        -not $backendReadme.Contains('生产部署会随应用启动自动创建或迁移数据库')) {
        throw "Generated backend guidance must explain the startup migration boundary."
    }

    Assert-MarkdownLinks $ProjectRoot
}

function Assert-ScenarioShape([string]$ProjectRoot, [string]$ProjectName, [hashtable]$Definition) {
    foreach ($relativePath in $Definition.Present) {
        $expandedPath = $relativePath.Replace('{name}', $ProjectName)
        if (-not (Test-Path -LiteralPath (Join-Path $ProjectRoot $expandedPath))) {
            throw "Scenario '$($Definition.Name)' is missing expected path: $expandedPath"
        }
    }
    foreach ($relativePath in $Definition.Absent) {
        $expandedPath = $relativePath.Replace('{name}', $ProjectName)
        if (Test-Path -LiteralPath (Join-Path $ProjectRoot $expandedPath)) {
            throw "Scenario '$($Definition.Name)' contains path that should be removed: $expandedPath"
        }
    }

    $readme = Get-Content -LiteralPath (Join-Path $ProjectRoot "README.md") -Raw -Encoding UTF8
    foreach ($expectedText in $Definition.ReadmeContains) {
        if (-not $readme.Contains($expectedText)) {
            throw "Scenario '$($Definition.Name)' README is missing expected text: $expectedText"
        }
    }
    foreach ($unexpectedText in $Definition.ReadmeExcludes) {
        if ($readme.Contains($unexpectedText)) {
            throw "Scenario '$($Definition.Name)' README contains disabled capability: $unexpectedText"
        }
    }
}

function Get-FreeTcpPort {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    try {
        return ([Net.IPEndPoint]$listener.LocalEndpoint).Port
    }
    finally {
        $listener.Stop()
    }
}

function Invoke-RuntimeSmoke([string]$ProjectRoot, [string]$Configuration) {
    $apiProject = Get-ChildItem -LiteralPath (Join-Path $ProjectRoot "backend/src") -Filter "*.Api.csproj" -Recurse | Select-Object -First 1
    $assemblyName = [IO.Path]::GetFileNameWithoutExtension($apiProject.Name)
    $apiAssembly = Join-Path $apiProject.DirectoryName "bin/$Configuration/net10.0/$assemblyName.dll"
    if (-not (Test-Path -LiteralPath $apiAssembly)) {
        throw "Built API assembly was not found: $apiAssembly"
    }

    $port = Get-FreeTcpPort
    $baseUrl = "http://127.0.0.1:$port"
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = (Get-Command dotnet -ErrorAction Stop).Source
    $startInfo.WorkingDirectory = $apiProject.DirectoryName
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.ArgumentList.Add($apiAssembly)
    $startInfo.Environment['ASPNETCORE_ENVIRONMENT'] = 'Development'
    $startInfo.Environment['ASPNETCORE_URLS'] = $baseUrl
    $startInfo.Environment['ConnectionStrings__Default'] = ''
    $startInfo.Environment['SpaProxy__Enabled'] = 'false'
    $startInfo.Environment['OAuth__DisableHttpsRequirement'] = 'true'

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $started = $false
    $failure = $null
    $stdout = ""
    $stderr = ""
    try {
        $started = $process.Start()
        if (-not $started) {
            throw "Failed to start generated API."
        }
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds(60)
        $healthy = $false
        while ([DateTimeOffset]::UtcNow -lt $deadline) {
            if ($process.HasExited) {
                $failure = "Generated API exited before becoming healthy with code $($process.ExitCode)."
                break
            }
            try {
                $response = Invoke-WebRequest -Uri "$baseUrl/api/health" -TimeoutSec 2 -ErrorAction Stop
                if ($response.StatusCode -eq 200) {
                    $healthy = $true
                    break
                }
            }
            catch {
                Start-Sleep -Milliseconds 500
            }
        }
        if (-not $healthy -and -not $failure) {
            $failure = "Generated API did not become healthy within 60 seconds."
        }
    }
    catch {
        $failure = $_.Exception.Message
    }
    finally {
        if ($started) {
            if (-not $process.HasExited) {
                $process.Kill($true)
            }
            $process.WaitForExit()
            $stdout = $stdoutTask.GetAwaiter().GetResult()
            $stderr = $stderrTask.GetAwaiter().GetResult()
        }
        $process.Dispose()
    }

    if ($failure) {
        $combinedLog = ($stdout + "`n" + $stderr).Trim()
        if ($combinedLog.Length -gt 4000) {
            $combinedLog = $combinedLog.Substring($combinedLog.Length - 4000)
        }
        throw "$failure`nGenerated API log:`n$combinedLog"
    }
}

$scenarioMap = [ordered]@{
    "default" = @{
        Arguments = @(); Frontend = $true; Lint = $true
        Present = @("backend/src/{name}.Api/Controllers/AuthController.cs", "backend/src/{name}.Application/Permissions")
        Absent = @("backend/src/{name}.Api/Controllers/NotificationsController.cs", "backend/src/{name}.Api/Controllers/ExternalAuthController.cs")
        ReadmeContains = @("本地账号", "OpenIddict")
        ReadmeExcludes = @("通知持久化", "外部身份提供方登录")
    }
    "minimal" = @{
        Arguments = @("--include-identity", "false"); Frontend = $true; Lint = $false
        Present = @("backend/src/{name}.Api/Program.cs")
        Absent = @("backend/src/{name}.Api/Controllers/AuthController.cs", "backend/src/{name}.Application/Permissions", "frontend/src/app/features/account")
        ReadmeContains = @("EF Core 数据访问")
        ReadmeExcludes = @("本地账号", "OpenIddict", "通知持久化", "外部身份提供方登录")
    }
    "no-roles" = @{
        Arguments = @("--include-roles", "false"); Frontend = $true; Lint = $false
        Present = @("backend/src/{name}.Api/Controllers/AuthController.cs")
        Absent = @("backend/src/{name}.Application/Permissions", "backend/src/{name}.Domain/Permissions")
        ReadmeContains = @("本地账号", "OpenIddict")
        ReadmeExcludes = @("用户、角色、权限以及超级管理员授权模型")
    }
    "notifications" = @{
        Arguments = @("--include-notifications", "true"); Frontend = $true; Lint = $true
        Present = @("backend/src/{name}.Api/Controllers/NotificationsController.cs", "frontend/src/app/core/services/notification-service.ts")
        Absent = @("backend/src/{name}.Api/Controllers/ExternalAuthController.cs")
        ReadmeContains = @("通知持久化", "OpenIddict")
        ReadmeExcludes = @("外部身份提供方登录")
    }
    "no-openiddict" = @{
        Arguments = @("--include-openiddict", "false"); Frontend = $true; Lint = $false
        Present = @("backend/src/{name}.Api/Controllers/AuthController.cs")
        Absent = @("backend/src/{name}.Api/Controllers/AuthorizationController.cs", "backend/src/{name}.Api/Controllers/OpenApplicationController.cs")
        ReadmeContains = @("本地账号")
        ReadmeExcludes = @("OpenIddict", "OAuth 2.0/OIDC Server")
    }
    "external-login" = @{
        Arguments = @("--include-external-login", "true"); Frontend = $true; Lint = $true
        Present = @("backend/src/{name}.Api/Controllers/ExternalAuthController.cs", "frontend/src/app/features/account/components/external-auth-callback")
        Absent = @("backend/src/{name}.Api/Controllers/NotificationsController.cs")
        ReadmeContains = @("外部身份提供方登录", "OpenIddict")
        ReadmeExcludes = @("通知持久化")
    }
}

foreach ($scenario in $Scenarios) {
    if (-not $scenarioMap.Contains($scenario)) {
        throw "Unknown scenario '$scenario'. Valid scenarios: $($scenarioMap.Keys -join ', ')"
    }
}

New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
Reset-Directory $generatedRoot
Reset-Directory $hiveRoot
if ($ReusePackages -and (Test-Path -LiteralPath $packagesRoot)) {
    New-Item -ItemType Directory -Path $packagesRoot -Force | Out-Null
}
else {
    Reset-Directory $packagesRoot
}
$env:NUGET_PACKAGES = $packagesRoot

$escapedFeedRoot = [Security.SecurityElement]::Escape($feedRoot)
$escapedPackagesRoot = [Security.SecurityElement]::Escape($packagesRoot)
$nugetConfig = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <config>
    <add key="globalPackagesFolder" value="$escapedPackagesRoot" />
  </config>
  <packageSources>
    <clear />
    <add key="local-feed" value="$escapedFeedRoot" />
    <add key="nuget.org" value="$nugetOrg" />
  </packageSources>
</configuration>
"@
[IO.File]::WriteAllText($nugetConfigPath, $nugetConfig, [Text.UTF8Encoding]::new($false))

if (-not $SkipPack) {
    Reset-Directory $feedRoot
    Invoke-External "dotnet" @("pack", "framework/Leistd.Framework.slnx", "-c", $Configuration, "-o", $feedRoot)
}
elseif (-not (Test-Path -LiteralPath $feedRoot)) {
    throw "-SkipPack requires an existing .tmp/local-feed."
}

Invoke-External "dotnet" @("new", "--debug:custom-hive", $hiveRoot, "install", $templateRoot, "--force")

$results = [System.Collections.Generic.List[object]]::new()
foreach ($scenario in $Scenarios) {
    $definition = $scenarioMap[$scenario]
    $definition["Name"] = $scenario
    $projectName = Get-ScenarioProjectName $scenario
    $projectRoot = Join-Path $generatedRoot $scenario
    $newArguments = @("new", "--debug:custom-hive", $hiveRoot, "fullstack-app", "-n", $projectName, "-o", $projectRoot, "--force") + $definition.Arguments
    Invoke-External "dotnet" $newArguments
    Assert-GeneratedProject $projectRoot
    Assert-ScenarioShape $projectRoot $projectName $definition

    $solution = Get-ChildItem -LiteralPath (Join-Path $projectRoot "backend") -Filter "*.sln" | Select-Object -First 1
    Invoke-External "dotnet" @("restore", $solution.FullName, "--configfile", $nugetConfigPath)
    Invoke-External "dotnet" @("build", $solution.FullName, "-c", $Configuration, "--no-restore")

    $runtimeValidated = $false
    if (-not $SkipRuntime) {
        Invoke-RuntimeSmoke $projectRoot $Configuration
        $runtimeValidated = $true
    }

    $testProjects = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot "backend") -Filter "*Tests.csproj" -Recurse)
    foreach ($testProject in $testProjects) {
        Invoke-External "dotnet" @("test", $testProject.FullName, "-c", $Configuration, "--no-build")
    }

    $frontendValidated = $false
    $lintValidated = $false
    if (-not $SkipFrontend -and $definition.Frontend) {
        $frontendRoot = Join-Path $projectRoot "frontend"
        $env:HUSKY = "0"
        Invoke-External "npm" @("ci") $frontendRoot
        if ($definition.Lint) {
            Invoke-External "npm" @("run", "lint") $frontendRoot
            $lintValidated = $true
        }
        Invoke-External "npm" @("run", "build") $frontendRoot
        $frontendValidated = $true
    }

    $results.Add([PSCustomObject]@{
        Scenario = $scenario
        Backend = "pass"
        Runtime = if ($runtimeValidated) { "pass" } else { "skipped" }
        Lint = if ($lintValidated) { "pass" } else { "skipped" }
        Frontend = if ($frontendValidated) { "pass" } else { "skipped" }
        Output = [IO.Path]::GetRelativePath($repoRoot, $projectRoot)
    })
}

$results | Format-Table -AutoSize
Write-Host "Template matrix passed for $($results.Count) scenario(s)." -ForegroundColor Green
