param(
    [string[]]$Scenarios = @("default", "minimal", "no-roles", "notifications", "no-openiddict", "external-login", "localization", "no-localization", "localization-notifications", "localization-external-login"),
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$SkipPack,
    [switch]$SkipFrontend,
    [switch]$SkipRuntime
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$tempRoot = Join-Path $repoRoot ".tmp"

# 每次运行独立的工作根：generated/hive/feed 都放在唯一 run 目录下，使上一轮残留的被锁目录
# （MSBuild 复用节点仍持有 *.Tasks.dll 句柄等）永不阻断本轮，也让多个 AI/终端可并行执行——
# 每个 run 自包含，互不写对方目录。用 PID + 高精度时间戳组合成 run id（脚本运行时确定，天然唯一）。
$runId = "{0}-{1}" -f $PID, (Get-Date -Format "yyyyMMddHHmmssfff")
$runRoot = Join-Path $tempRoot (Join-Path "runs" $runId)

# 禁用 MSBuild 节点复用：worker 节点默认 /nodeReuse:true，进程退出后仍常驻并持有生成目录/包缓存的文件句柄，
# 导致后续清理失败。MSBUILDDISABLENODEREUSE=1 才是关闭 node reuse 的正确开关（DOTNET_CLI_USE_MSBUILD_SERVER
# 关的是另一个「MSBuild Server」特性，不影响 node reuse）。
$env:MSBUILDDISABLENODEREUSE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

# 第三方 NuGet 缓存跨 run 共享、只读复用：nuget.org 的包按 (id,version) 内容不可变，可安全并发共享（NuGet 自带
# 文件锁），避免每轮重下近 1GB 依赖闭包。真正会「同版本内容变化」的只有本地 pack 的 Leistd.*——restore 前定点
# 清除缓存里的 Leistd.* 强制重新解包（见下），其余保持温热。
$sharedPackagesRoot = Join-Path $tempRoot "nuget-cache"
# 本地 Leistd 包源位置随模式而定：
#  - 正常模式（本轮 pack）：per-run 独立目录，两个并行 run 各 pack 各的，杜绝共享目录重置竞争（发现#1）。
#  - -SkipPack：消费预先 pack 到共享 .tmp/local-feed 的包（CI 先 `dotnet pack -o .tmp/local-feed` 再 -SkipPack），
#    只读复用、并发安全。
$sharedFeedRoot = Join-Path $tempRoot "local-feed"
$feedRoot = if ($SkipPack) { $sharedFeedRoot } else { Join-Path $runRoot "local-feed" }
$generatedRoot = Join-Path $runRoot "generated-template"
$hiveRoot = Join-Path $runRoot "template-hive"
$nugetConfigPath = Join-Path $runRoot "template-matrix.NuGet.Config"
$lockFile = Join-Path $runRoot ".run.lock"            # 活动锁：清理旧 run 时据此/据修改时间判活，绝不删正在运行的 run
$staleRunAgeHours = 2                                  # 超过此时长且非本 run 的目录才视为陈旧、可清理
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
        # Windows 下 node/dotnet 残留句柄常导致 "目录不是空的"/访问被拒——对 IO/权限异常带退避重试，
        # 避免在打包/构建/测试前就阻断整个矩阵；最终仍失败时输出残留路径便于排查。
        $maxAttempts = 5
        for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
            try {
                Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
                break
            }
            catch [System.IO.IOException], [System.UnauthorizedAccessException] {
                if ($attempt -eq $maxAttempts) {
                    Write-Warning "无法清理目录（$maxAttempts 次重试后仍失败）: $Path"
                    throw
                }
                Start-Sleep -Milliseconds (200 * $attempt)
            }
        }
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
        ".agents/skills/spartan/SKILL.md",
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
        "backend/README.md",
        "frontend/components.json"
    )
    foreach ($relativePath in $requiredFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $ProjectRoot $relativePath))) {
            throw "Generated project is missing required guidance: $relativePath"
        }
    }

    $skillRoot = Join-Path $ProjectRoot ".agents/skills"
    $skillNames = @(Get-ChildItem -LiteralPath $skillRoot -Directory | ForEach-Object Name)
    # 生成项目预期携带 leistd-project-workflow（项目协作）与 spartan（前端 UI 库 CLI 用法）。
    $allowedSkills = @("leistd-project-workflow", "spartan")
    if (-not ($skillNames -contains "leistd-project-workflow")) {
        throw "Generated project must contain leistd-project-workflow in $skillRoot"
    }
    $unexpectedSkills = @($skillNames | Where-Object { $allowedSkills -notcontains $_ })
    if ($unexpectedSkills.Count -gt 0) {
        throw "Generated project has unexpected skills in $skillRoot : $($unexpectedSkills -join ', ')"
    }

    $projectReadme = Get-Content -LiteralPath (Join-Path $ProjectRoot "README.md") -Raw -Encoding UTF8
    foreach ($marker in @("npx skills add ./.agents/skills/leistd-project-workflow", "--agent claude-code", "--copy", "skills-lock.json")) {
        if (-not $projectReadme.Contains($marker)) {
            throw "Generated project README is missing AI CLI compatibility guidance: $marker"
        }
    }

    $expectedStandards = @("api.md", "coding-backend.md", "coding-common.md", "coding-frontend.md", "project-structure.md", "service-invocation.md", "tech-stack.md", "testing.md", "ui-design.md")
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

    if ($Definition.ForbiddenTokens) {
        $sourceRoots = @("backend/src", "frontend/src", "frontend/_mock") |
            ForEach-Object { Join-Path $ProjectRoot $_ } |
            Where-Object { Test-Path -LiteralPath $_ }

        $sourceFiles = Get-ChildItem -LiteralPath $sourceRoots -Recurse -File -Include *.cs, *.ts -ErrorAction SilentlyContinue
        foreach ($file in $sourceFiles) {
            $content = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
            foreach ($token in $Definition.ForbiddenTokens) {
                if ($content -and $content.Contains($token)) {
                    $relative = $file.FullName.Substring($ProjectRoot.Length).TrimStart('/', '\')
                    throw "Scenario '$($Definition.Name)' leaks trimmed contract token '$token' in $relative"
                }
            }
        }
    }

    $mockInterceptorPath = Join-Path $ProjectRoot "frontend/_mock/core/interceptor.ts"
    $mockInterceptor = Get-Content -LiteralPath $mockInterceptorPath -Raw -Encoding UTF8
    foreach ($marker in @("MOCK_ROUTE_NOT_FOUND", "Mock Route Not Found", "startsWith('/api/')")) {
        if (-not $mockInterceptor.Contains($marker)) {
            throw "Scenario '$($Definition.Name)' Mock interceptor is missing fail-fast marker: $marker"
        }
    }

    $notificationServicePath = Join-Path $ProjectRoot "frontend/src/app/layout/components/notifications/notification-service.ts"
    if (Test-Path -LiteralPath $notificationServicePath) {
        $notificationService = Get-Content -LiteralPath $notificationServicePath -Raw -Encoding UTF8
        foreach ($marker in @("environment.useMock", "useMock.enable", "await this.signalR.connect()")) {
            if (-not $notificationService.Contains($marker)) {
                throw "Scenario '$($Definition.Name)' notification service is missing Mock isolation marker: $marker"
            }
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
        # 健康探测窗口。生成项目单独冷启动通常 ~10s 即 200；但矩阵在一次 pack+generate+restore+build 突发之后
        # 立即启动首个场景，CPU/磁盘/句柄仍处饱和，冷启动可显著变慢——旧的 60s 会偶发假超时（非启动故障，
        # 单独启动即健康）。放宽到 150s 覆盖满载冷启动尾延迟；可用 $env:MATRIX_HEALTH_TIMEOUT_SEC 覆盖。
        $healthTimeoutSec = 150
        if ($env:MATRIX_HEALTH_TIMEOUT_SEC) { $healthTimeoutSec = [int]$env:MATRIX_HEALTH_TIMEOUT_SEC }
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds($healthTimeoutSec)
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
            $failure = "Generated API did not become healthy within $healthTimeoutSec seconds."
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
        # 路径存在性挡不住"文件还在、角色契约残留在里面"：DTO 字段、OAuth scope、role claim
        # 都会让前后端契约对不上，或让 Mock 与真实后端行为分叉。只查高信号符号，
        # 不做泛化的 "role" 扫描——HTML/ARIA 里到处是 role=，噪声会淹掉信号。
        # 不含裸的 Claims.Role：它还作为 ClaimsIdentity(authType, nameType, roleType) 的构造参数出现，
        # 那是框架管道而非角色契约（该重载没有两参版本，省掉会连带改变 nameType 解析）。
        # scp:roles 是前端字面量，不含任何 C# 符号——上一轮只查符号，它就整条漏了过去。
        ForbiddenTokens = @("roleIds", "Scopes.Roles", "SetClaims(Claims.Role", "ManageRoles", "scp:roles")
    }
    "notifications" = @{
        Arguments = @("--include-notifications", "true"); Frontend = $true; Lint = $true
        Present = @("backend/src/{name}.Api/Controllers/NotificationsController.cs", "frontend/src/app/layout/components/notifications/notification-service.ts")
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
    "localization" = @{
        Arguments = @("--include-localization", "true"); Frontend = $true; Lint = $true
        Present = @("backend/src/{name}.Api/Resources/en.json", "backend/src/{name}.Api/Resources/zh-CN.json", "frontend/public/i18n/en.json", "frontend/src/app/core/services/language-service.ts")
        Absent = @()
        ReadmeContains = @()
        ReadmeExcludes = @()
    }
    "no-localization" = @{
        Arguments = @("--include-localization", "false"); Frontend = $true; Lint = $true
        Present = @("backend/src/{name}.Api/Program.cs")
        Absent = @("backend/src/{name}.Api/Resources", "frontend/public/i18n", "frontend/src/app/core/services/language-service.ts")
        ReadmeContains = @()
        ReadmeExcludes = @()
    }
    # 交叉场景：本地化 + 通知（校验通知面板的本地化 gate）
    "localization-notifications" = @{
        Arguments = @("--include-localization", "true", "--include-notifications", "true"); Frontend = $true; Lint = $true
        Present = @("frontend/public/i18n/en.json", "backend/src/{name}.Api/Controllers/NotificationsController.cs")
        Absent = @()
        ReadmeContains = @()
        ReadmeExcludes = @()
    }
    # 交叉场景：本地化 + 外部登录（校验第三方回调页的本地化 gate）
    "localization-external-login" = @{
        Arguments = @("--include-localization", "true", "--include-external-login", "true"); Frontend = $true; Lint = $true
        Present = @("frontend/public/i18n/en.json", "frontend/src/app/features/account/components/external-auth-callback")
        Absent = @()
        ReadmeContains = @()
        ReadmeExcludes = @()
    }
}

foreach ($scenario in $Scenarios) {
    if (-not $scenarioMap.Contains($scenario)) {
        throw "Unknown scenario '$scenario'. Valid scenarios: $($scenarioMap.Keys -join ', ')"
    }
}

New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
[IO.File]::WriteAllText($lockFile, ("pid={0} started={1}" -f $PID, (Get-Date -Format "o")), [Text.UTF8Encoding]::new($false))

# 安全门禁：生产依赖闭包不得含 high 及以上漏洞。10 个场景共用模板的两份 lockfile，
# 故在循环前各审计一次即可；advisory 端点偶发抖动，用重试消化（完整审计属依赖治理任务）。
if (-not $SkipFrontend) {
    $auditTargets = @(
        (Join-Path $repoRoot "template/frontend"),
        (Join-Path $repoRoot "template/.template.config/localization/frontend")
    )
    foreach ($auditRoot in $auditTargets) {
        for ($auditAttempt = 1; $auditAttempt -le 3; $auditAttempt++) {
            try {
                Invoke-External "npm" @("audit", "--omit=dev", "--audit-level=high", "--package-lock-only") $auditRoot
                break
            }
            catch {
                if ($auditAttempt -ge 3) { throw }
                Start-Sleep -Seconds (5 * $auditAttempt)
            }
        }
    }
}

# 清理陈旧 run 目录：仅删「非本 run」且「超过 $staleRunAgeHours 未活动」的目录，绝不删正在运行的 run——
# 支持多 AI/终端并行执行。活动判据：目录里 .run.lock（无则回退目录本身）的最后写入时间。被锁清不掉也无妨（尽力而为）。
$oldRunsRoot = Join-Path $tempRoot "runs"
$staleBefore = (Get-Date).AddHours(-$staleRunAgeHours)
if (Test-Path -LiteralPath $oldRunsRoot) {
    Get-ChildItem -LiteralPath $oldRunsRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -ne $runRoot } |
        Where-Object {
            $lock = Join-Path $_.FullName ".run.lock"
            # TOCTOU 安全：Test-Path 与读取之间锁可能被并行 run 删除，Get-Item 加 SilentlyContinue 返回 $null 后回退目录时间，
            # 避免在 $ErrorActionPreference=Stop 下因「文件已消失」中止整轮清理（进而中止整轮门禁）。
            $lockItem = if (Test-Path -LiteralPath $lock) { Get-Item -LiteralPath $lock -ErrorAction SilentlyContinue } else { $null }
            $activityTime = if ($lockItem) { $lockItem.LastWriteTime } else { $_.LastWriteTime }
            $activityTime -lt $staleBefore
        } |
        ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }
}

New-Item -ItemType Directory -Path $generatedRoot -Force | Out-Null
New-Item -ItemType Directory -Path $hiveRoot -Force | Out-Null
# 第三方 NuGet 缓存跨 run 共享、只读复用（(id,version) 不可变，并发安全）；本 run 用它作 globalPackagesFolder。
New-Item -ItemType Directory -Path $sharedPackagesRoot -Force | Out-Null
$env:NUGET_PACKAGES = $sharedPackagesRoot

$escapedFeedRoot = [Security.SecurityElement]::Escape($feedRoot)
$escapedPackagesRoot = [Security.SecurityElement]::Escape($sharedPackagesRoot)
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
    throw "-SkipPack requires an existing shared feed at '$feedRoot'（先 `dotnet pack ... -o .tmp/local-feed`，或省略 -SkipPack 以重新 pack）."
}

# 定点清除共享缓存里的 Leistd.*：NuGet 对已在 globalPackagesFolder 中的同版本包不会重新解包，
# 若源码变了但版本号未变（本地 0.12.0），restore 会命中陈旧内容。只清 Leistd.*（几 MB，非整个 ~1GB 闭包）
# 强制本轮重新解包新 pack 的本地包；第三方包保持温热。
Get-ChildItem -LiteralPath $sharedPackagesRoot -Directory -Filter "leistd.*" -ErrorAction SilentlyContinue |
    ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }

Invoke-External "dotnet" @("new", "--debug:custom-hive", $hiveRoot, "install", $templateRoot, "--force")

$results = [System.Collections.Generic.List[object]]::new()
foreach ($scenario in $Scenarios) {
    # 心跳：刷新锁文件时间戳，防止长时间运行的 run 被并行进程按 stale 目录清理。
    [IO.File]::SetLastWriteTimeUtc($lockFile, [DateTime]::UtcNow)
    $definition = $scenarioMap[$scenario]
    $definition["Name"] = $scenario
    $projectName = Get-ScenarioProjectName $scenario
    $projectRoot = Join-Path $generatedRoot $scenario
    $newArguments = @("new", "--debug:custom-hive", $hiveRoot, "fullstack-app", "-n", $projectName, "-o", $projectRoot, "--force") + $definition.Arguments
    Invoke-External "dotnet" $newArguments
    Assert-GeneratedProject $projectRoot
    Assert-ScenarioShape $projectRoot $projectName $definition

    $solution = Get-ChildItem -LiteralPath (Join-Path $projectRoot "backend") -Filter "*.sln" | Select-Object -First 1
    # --force：等价于删除并重建 project.assets.json、强制重新评估依赖资产图（解决被 design-time restore
    # 覆盖、资产图未刷新等问题）。注意：它**不**清除 globalPackagesFolder 中已提取的同 ID/同版本包——
    # 同版本内容变化（本地 Leistd.* 0.12.0）由前面「pack 后定点清除共享缓存里的 leistd.*」处理，故此处安全。
    Invoke-External "dotnet" @("restore", $solution.FullName, "--configfile", $nugetConfigPath, "--force")
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
    $testValidated = $false
    if (-not $SkipFrontend -and $definition.Frontend) {
        $frontendRoot = Join-Path $projectRoot "frontend"
        $env:HUSKY = "0"
        Invoke-External "npm" @("ci") $frontendRoot
        Invoke-External "npx" @("ng", "g", "@spartan-ng/cli:info", "--json") $frontendRoot
        Invoke-External "npx" @("ng", "g", "@spartan-ng/cli:healthcheck") $frontendRoot
        if ($definition.Lint) {
            Invoke-External "npm" @("run", "lint") $frontendRoot
            $lintValidated = $true
        }
        Invoke-External "npm" @("run", "build") $frontendRoot
        $frontendValidated = $true

        # 前端单测（无头、单次）：每个场景都含一条不受本地化裁剪的基础 smoke spec，
        # 故 npm test 恒能命中 >=1 个 spec；本地化场景另含 translationReady 首帧回归测试。
        Invoke-External "npm" @("test", "--", "--watch=false", "--browsers=ChromeHeadless") $frontendRoot
        $testValidated = $true
    }

    $results.Add([PSCustomObject]@{
        Scenario = $scenario
        Backend = "pass"
        Runtime = if ($runtimeValidated) { "pass" } else { "skipped" }
        Lint = if ($lintValidated) { "pass" } else { "skipped" }
        Frontend = if ($frontendValidated) { "pass" } else { "skipped" }
        Test = if ($testValidated) { "pass" } else { "skipped" }
        Output = [IO.Path]::GetRelativePath($repoRoot, $projectRoot)
    })
}

$results | Format-Table -AutoSize
Write-Host "Template matrix passed for $($results.Count) scenario(s)." -ForegroundColor Green
