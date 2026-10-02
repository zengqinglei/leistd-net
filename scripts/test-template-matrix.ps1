param(
    # 不给就跑全量：全量清单是 $AllScenarios（见下），不写死在这里，
    # 否则「加了场景定义却忘了加进清单」会让新场景静默不跑——下面有断言兜住
    [string[]]$Scenarios = @(),
    # 档位：full 为全集，pr 为 PR 档子集（归属见 template-matrix-scenarios.ps1）。不能与人工场景选择混用；
    # 都不给时等同 full。
    [ValidateSet("pr", "full")]
    [string]$Tier,
    # 固定 CI 分片，须与 -Tier 同用。
    [ValidateSet(1, 2)]
    [int]$Shard,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [ValidateSet("chromiumHeadless", "chromium")]
    [string]$FrontendBrowser = "chromiumHeadless",
    [switch]$SkipPack,
    [string]$LocalFeedPath,
    [switch]$SkipFrontend,
    [switch]$SkipRuntime,
    # 只为选中的场景构建并运行容器入口；Dockerfile/Compose 变化时使用，不随每个普通代码修改运行。
    [string[]]$ContainerSmokeScenarios = @(),
    # 在登记的容器场景（若在本片）上执行容器检查；CI 用它，不在 workflow 重抄场景名。
    [switch]$ContainerSmoke
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$tempRoot = Join-Path $repoRoot ".tmp"
$tempPrefix = [IO.Path]::GetFullPath($tempRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar

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

# globalPackagesFolder 必须也按 run 隔离。本地 Leistd 包在开发期间会在版本号不变时重新 pack；
# 共享解包目录并定点清理会在并发 build 期间抽走 DLL。每个 run 独立解包，NuGet HTTP 缓存仍可复用下载内容。
$packagesRoot = Join-Path $runRoot "nuget-cache"
# 本地 Leistd 包源位置随模式而定：
#  - 正常模式（本轮 pack）：per-run 独立目录，两个并行 run 各 pack 各的，杜绝共享目录重置竞争（发现#1）。
#  - -SkipPack：消费预先 pack 到共享 .tmp/local-feed 的包（CI 先 `pack-local-feed.ps1` 再 -SkipPack），
#    只读复用、并发安全。
$sharedFeedRoot = if ($LocalFeedPath) { [IO.Path]::GetFullPath($LocalFeedPath, $repoRoot) } else { Join-Path $tempRoot "local-feed" }
if ($LocalFeedPath -and -not $SkipPack) { throw "-LocalFeedPath requires -SkipPack." }
if ($LocalFeedPath) {
    if (-not $sharedFeedRoot.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "-LocalFeedPath must remain inside .tmp: $sharedFeedRoot"
    }
}
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

# 与 Invoke-External 同型，但显式关闭子进程的 stdin。
# 用于那些会无条件发问、又没有非交互开关的 CLI：拿到 EOF 即取默认值，
# 从而让脚本在交互终端下也保持无人值守。
function Invoke-ExternalWithClosedInput([string]$Command, [string[]]$Arguments, [string]$WorkingDirectory = $repoRoot) {
    Write-Host "> $Command $($Arguments -join ' ')  (stdin closed)" -ForegroundColor DarkGray
    Push-Location $WorkingDirectory
    try {
        $null | & $Command @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "Command failed with exit code ${LASTEXITCODE}: $Command $($Arguments -join ' ')"
        }
    }
    finally {
        Pop-Location
    }
}

# 与 Invoke-External 同形，但把标准输出交回调用方（用于要读取命令结果的自检）。
function Invoke-ExternalCapture([string]$Command, [string[]]$Arguments, [string]$WorkingDirectory = $repoRoot) {
    Write-Host "> $Command $($Arguments -join ' ')" -ForegroundColor DarkGray
    Push-Location $WorkingDirectory
    try {
        $output = & $Command @Arguments 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw ("Command failed with exit code ${LASTEXITCODE}: $Command $($Arguments -join ' ')`n" +
                ($output -join "`n"))
        }
        return ($output | ForEach-Object { [string]$_ })
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
        ".agents/skills/leistd-project-workflow/references/delivery.md",
        ".agents/skills/leistd-project-workflow/references/development.md",
        ".agents/skills/leistd-project-workflow/references/quality.md",
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
    # 生成项目携带项目协作和前端 UI 两个独立入口。
    $allowedSkills = @("leistd-project-workflow", "spartan")
    if (@($allowedSkills | Where-Object { $skillNames -notcontains $_ }).Count -gt 0) {
        throw "Generated project is missing a required skill in $skillRoot"
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
    if ($programText.Contains("MigrateAsync(") -or $programText.Contains("EnsureCreatedAsync(")) {
        throw "Generated API must not mutate database schema during startup."
    }

    $backendReadme = Get-Content -LiteralPath (Join-Path $ProjectRoot "backend/README.md") -Raw -Encoding UTF8
    if (-not $backendReadme.Contains('DbMigrator') -or
        -not $backendReadme.Contains('启动时不自动迁移')) {
        throw "Generated backend guidance must explain the independent DbMigrator boundary."
    }

    Assert-MarkdownLinks $ProjectRoot
}

# 本地化产物与生成源码一一对应，递归核对 scope 文件都已由 postbuild 展平。

function Assert-OptimizedTranslations([string]$FrontendRoot) {
    $source = Join-Path $FrontendRoot "public/i18n"
    if (-not (Test-Path $source)) { return }
    $package = Get-Content -LiteralPath (Join-Path $FrontendRoot "package.json") -Raw | ConvertFrom-Json
    $output = Join-Path $FrontendRoot "dist/$($package.name)/browser/i18n"
    $sourceFiles = @(Get-ChildItem -LiteralPath $source -Recurse -File -Filter '*.json')
    $outputFiles = @(Get-ChildItem -LiteralPath $output -Recurse -File -Filter '*.json')
    if ($sourceFiles.Count -ne $outputFiles.Count) { throw "Translation file count changed during production build." }
    function Get-TranslationKeys($Node, [string]$Prefix = '') {
        foreach ($property in $Node.PSObject.Properties) {
            $key = if ($Prefix) { "$Prefix.$($property.Name)" } else { $property.Name }
            if ($property.Value -is [pscustomobject]) { Get-TranslationKeys $property.Value $key }
            else { $key }
        }
    }
    foreach ($file in $sourceFiles) {
        $relative = [IO.Path]::GetRelativePath($source, $file.FullName)
        $built = Get-Content -LiteralPath (Join-Path $output $relative) -Raw | ConvertFrom-Json
        if (@($built.PSObject.Properties | Where-Object { $_.Value -isnot [string] }).Count -gt 0) {
            throw "Translation file was not flattened: $relative"
        }
        $original = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
        $expected = @(Get-TranslationKeys $original | Sort-Object)
        $actual = @($built.PSObject.Properties.Name | Sort-Object)
        if (Compare-Object $expected $actual -CaseSensitive) { throw "Translation keys changed during optimization: $relative" }
    }
    Write-Host "Validated $($sourceFiles.Count) flattened global/scope translation files."
}

# 磁盘上的每个 spec 文件都必须被测试发现。
#
# 这一条防的是"恒绿"：`_mock/**/*.spec.ts` 从加进模板那天起就没被执行过
# （builder 的 findTests 以 sourceRoot 为 glob 工作目录，`_mock/**` 被解析成不存在的 `src/_mock/**`），
# 而测试照样全绿——比一条失败的用例更坏，因为它让人以为那一层有覆盖。Karma 时代同样如此，
# 不是 Vitest 迁移引入的。
#
# 用 builder 自己的 --list-tests：判据与真实运行共用同一套发现逻辑，不另写一份 glob 去猜。
# 判据是**路径集合**而不是文件数：数量相等而集合不同是可能的（同时改名与挪目录）。
# 只报缺失项——报告里出现磁盘上没有的文件属于工具问题，不是模板要守的约定。
function Assert-EveryFrontendSpecDiscovered([string]$FrontendRoot) {
    $onDisk = @(
        Get-ChildItem -LiteralPath $FrontendRoot -Recurse -File -Filter "*.spec.ts" |
            Where-Object { $_.FullName -notmatch "[\\/]node_modules[\\/]" } |
            ForEach-Object { [IO.Path]::GetRelativePath($FrontendRoot, $_.FullName).Replace('\', '/') }
    )
    if ($onDisk.Count -eq 0) {
        throw "No *.spec.ts found under $FrontendRoot; the self-check would pass vacuously."
    }

    $listed = Invoke-ExternalCapture "npm" @("test", "--", "--list-tests") $FrontendRoot
    $discovered = @(
        $listed | ForEach-Object { $_.Trim() } | Where-Object { $_ -like "*.spec.ts" } |
            ForEach-Object {
                $path = $_
                if ([IO.Path]::IsPathRooted($path)) {
                    [IO.Path]::GetRelativePath($FrontendRoot, $path).Replace('\', '/')
                } else {
                    $path.Replace('\', '/')
                }
            }
    )

    $discoveredSet = [System.Collections.Generic.HashSet[string]]::new([string[]]$discovered, [StringComparer]::Ordinal)
    $missing = @($onDisk | Where-Object { -not $discoveredSet.Contains($_) })
    if ($missing.Count -gt 0) {
        throw ("These spec files exist on disk but are not discovered by the test builder. The `include` " +
            "patterns in angular.json resolve relative to sourceRoot, so paths outside it need `../`:`n  " +
            ($missing -join "`n  "))
    }

    Write-Host ("  每个 spec 都会被发现（{0} 个）。" -f $onDisk.Count)
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

    # RequiredTokens：指定文件必须含指定片段。用于断言"剪裁后留下的是对的那一份"，
    # 与 ForbiddenTokens（不该留的没留下）互补——只查缺失会漏掉"两份都在"的情形
    if ($Definition.RequiredTokens) {
        foreach ($entry in $Definition.RequiredTokens.GetEnumerator()) {
            $relativePath = $entry.Key.Replace("{name}", $ProjectName)
            $filePath = Join-Path $ProjectRoot $relativePath
            if (-not (Test-Path -LiteralPath $filePath)) {
                throw "Scenario '$($Definition.Name)' is missing required file: $relativePath"
            }

            $content = Get-Content -LiteralPath $filePath -Raw -Encoding UTF8
            foreach ($token in $entry.Value) {
                if (-not ($content -and $content.Contains($token))) {
                    throw "Scenario '$($Definition.Name)' file $relativePath is missing required token '$token'"
                }
            }
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
        foreach ($marker in @("environment.useMock", "isMockedUrl(", "await this.signalR.connect()")) {
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
    $startInfo.Environment['Database__InMemoryName'] = "MatrixRuntime-$([Guid]::NewGuid().ToString('N'))"
    $startInfo.Environment['OAuth__DisableHttpsRequirement'] = 'true'
    # 不注入管理员口令：新内存库首次启动要建管理员，口令取自生成项目的 appsettings.Development.json，
    # 这正是克隆后直接运行的路径
    $startInfo.Environment['VerificationCodes__Key'] = 'AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8='
    # Resource 形态的签发方在基线配置里刻意留空、组合期必填；其余形态不读这一项。
    # 冒烟只探存活，不回源，给一个不可达的地址即可
    $startInfo.Environment['Authentication__Issuer'] = 'https://identity.matrix.test/'
    $startInfo.Environment['Authentication__ClientId'] = 'matrix-resource'
    $startInfo.Environment['Authentication__ClientSecret'] = 'matrix-resource-secret'

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
                $response = Invoke-WebRequest -Uri "$baseUrl/api/health/live" -TimeoutSec 2 -ErrorAction Stop
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

. (Join-Path $PSScriptRoot "template-matrix-scenarios.ps1")

if ($PSBoundParameters.ContainsKey("Shard") -and -not $Tier) { throw "-Shard requires -Tier." }
if ($ContainerSmoke -and -not $Tier) { throw "-ContainerSmoke requires -Tier." }
if ($Tier) {
    if ($Scenarios.Count -gt 0) { throw "-Tier and -Scenarios cannot be combined." }
    $Scenarios = Get-TierScenarios $Tier $Shard
    if ($Scenarios.Count -eq 0) { throw "Tier $Tier shard $Shard has no scenarios." }
    if ($ContainerSmoke -and $ContainerScenario -in $Scenarios) {
        $ContainerSmokeScenarios += $ContainerScenario
    }
}

if ($Scenarios.Count -eq 0) {
    $Scenarios = $AllScenarios
}

foreach ($scenario in $Scenarios) {
    if (-not $scenarioMap.Contains($scenario)) {
        throw "Unknown scenario '$scenario'. Valid scenarios: $($AllScenarios -join ', ')"
    }
}
foreach ($scenario in $ContainerSmokeScenarios) {
    if ($scenario -notin $Scenarios) {
        throw "Container smoke scenario '$scenario' must also be selected with -Scenarios."
    }
}

New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
[IO.File]::WriteAllText($lockFile, ("pid={0} started={1}" -f $PID, (Get-Date -Format "o")), [Text.UTF8Encoding]::new($false))

# 安全门禁：生产依赖闭包不得含 high 及以上漏洞。所有场景共用模板的两份 lockfile，
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
New-Item -ItemType Directory -Path $packagesRoot -Force | Out-Null
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
    throw "-SkipPack requires an existing feed at '$feedRoot'（先 `pwsh framework/build/pack-local-feed.ps1`，或省略 -SkipPack 以重新 pack）."
}

# 生成前先校验符号一致性：悬空引用、注释里的指令字面形式、恒真嵌套这三类问题，
# 模板引擎要么抛只有文件名的 NullReferenceException、要么静默少生成整段代码，
# 到那一步再排查代价极高（本仓库为此付过一整轮）。在这里拦住。
Invoke-External "pwsh" @("-File", (Join-Path $repoRoot "scripts/check-template-symbols.ps1"))

# Python 闸门的解释器：CI/Unix 常为 python3，Windows 通常只有 python（约定见 docs/framework/development-guide.md §9）
$pythonCmd = $null
foreach ($candidate in @("python3", "python")) {
    $found = Get-Command $candidate -ErrorAction SilentlyContinue
    if ($found -and ((& $found.Name --version 2>&1) -match 'Python 3\.')) { $pythonCmd = $found.Name; break }
}
if (-not $pythonCmd) { throw "未找到 Python 3 解释器（python3/python），无法运行 Python 静态闸门。" }

# using 守卫窄于用法 —— 生成后表现为 CS0246，而下面只编译 $Scenarios 里的几个场景，
# 排不到的符号组合要等真实使用者踩。这一步在全部符号取值上求值，比矩阵严。
Invoke-External $pythonCmd @((Join-Path $repoRoot "scripts/check-using-guards.py"))

# 连接解析路径上的 sync-over-async。基线验收标准第 6 条此前只是文字声明，
# 没有任何检查兜着；这条路径每次取 DbContext 都执行，阻塞会在线程池饥饿下自我放大。
Invoke-External $pythonCmd @((Join-Path $repoRoot "scripts/check-async-boundaries.py"))

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
    # --force 重建 project.assets.json；globalPackagesFolder 是本 run 私有目录，不会命中其他 run 的同版本 Leistd 内容。
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
        # healthcheck 末尾会无条件询问"是否升级依赖"，默认 N。
        # 该 CLI 没有能覆盖这个提示的非交互开关——实测 --interactive=false、--defaults
        # 与 CI=true 三者都不生效（提示由它自带的提示库发出，不走 Angular schematic 提示）。
        # 因此显式把 stdin 关掉：拿到 EOF 就取默认值 N，有无 TTY 行为一致，
        # 不依赖"调用方恰好重定向了 stdin"
        Invoke-ExternalWithClosedInput "npx" @("ng", "g", "@spartan-ng/cli:healthcheck") $frontendRoot
        if ($definition.Lint) {
            Invoke-External "npm" @("run", "lint") $frontendRoot
            $lintValidated = $true
        }
        Invoke-External "npm" @("run", "build") $frontendRoot
        Assert-OptimizedTranslations $frontendRoot
        $frontendValidated = $true

        # 前端单测（单次）：CI 默认 chromiumHeadless，人工验收可传 -FrontendBrowser chromium 观看有头浏览器。
        # 每个场景都含一条不受本地化裁剪的基础 smoke spec；本地化场景另含语言切换「先加载再激活」与首帧词条的回归测试。
        # 先核对"磁盘上的每个 spec 都会被发现"，再真的跑。放在前面是因为它更快（--list-tests 不构建也不执行），
        # 而且这条不通过时后面那轮绿灯是假的。
        Assert-EveryFrontendSpecDiscovered $frontendRoot
        Invoke-External "npm" @("test", "--", "--watch=false", "--browsers=$FrontendBrowser") $frontendRoot
        $testValidated = $true
    }

    $containerValidated = $false
    if ($scenario -in $ContainerSmokeScenarios) {
        # 候选 Framework 包可能尚未发布。只在本轮生成目录注入本地包和 NuGet.Config，
        # 让镜像验证消费的仍是同一候选源码；交付模板不包含这个临时包源。
        $containerFeed = Join-Path $projectRoot "backend/.local-feed"
        New-Item -ItemType Directory -Path $containerFeed -Force | Out-Null
        Copy-Item -Path (Join-Path $feedRoot "*.nupkg") -Destination $containerFeed
        $containerNuGetConfig = @'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="candidate" value="/src/backend/.local-feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="candidate"><package pattern="Leistd.*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
'@
        [IO.File]::WriteAllText((Join-Path $projectRoot "backend/NuGet.Config"), $containerNuGetConfig)
        $apiImage = "leistd-template-smoke-api:$runId"
        $migratorImage = "leistd-template-smoke-migrator:$runId"
        try {
            Invoke-External "docker" @("build", "--target", "api", "-t", $apiImage, $projectRoot)
            Invoke-External "docker" @("build", "--target", "migrator", "-t", $migratorImage, $projectRoot)
            Invoke-External "docker" @("run", "--rm", "--entrypoint", "dotnet", $apiImage, "--info")
            Invoke-External "docker" @("run", "--rm", "--entrypoint", "dotnet", $migratorImage, "--info")
            $containerValidated = $true
        }
        finally {
            & docker image rm $apiImage $migratorImage 2>$null | Out-Null
            $global:LASTEXITCODE = 0
        }
    }

    $results.Add([PSCustomObject]@{
        Scenario = $scenario
        Backend = "pass"
        Runtime = if ($runtimeValidated) { "pass" } else { "skipped" }
        Lint = if ($lintValidated) { "pass" } else { "skipped" }
        Frontend = if ($frontendValidated) { "pass" } else { "skipped" }
        Test = if ($testValidated) { "pass" } else { "skipped" }
        Container = if ($containerValidated) { "pass" } else { "skipped" }
        Output = [IO.Path]::GetRelativePath($repoRoot, $projectRoot)
    })
}

$results | Format-Table -AutoSize
Write-Host "Template matrix passed for $($results.Count) scenario(s)." -ForegroundColor Green

# 只在所有阶段成功后产出证明；汇总作业核对场景全集和阶段，缺片不得假绿。
$resultFile = Join-Path $runRoot "matrix-shard-$Shard.json"
[PSCustomObject]@{ Tier = $Tier; Shard = $Shard; Results = @($results) } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $resultFile -Encoding utf8
if ($env:GITHUB_OUTPUT) { "results_path=$resultFile" | Out-File $env:GITHUB_OUTPUT -Append }
