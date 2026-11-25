#!/usr/bin/env pwsh

<#
.SYNOPSIS
    本地与 CI 共用的验证入口。默认完整回归，-Fast 用于本地快速检查。

.PARAMETER Fast
    跑静态检查、后端还原、构建、风格检查和单元测试；有前端时另跑 lint，
    有本地身份时另查 E2E 用例登记。使用已有的 node_modules，不需要 Docker。

.PARAMETER List
    输出 full 和 fast 两种模式的步骤清单，不执行。

.PARAMETER ChangedBase
    以该提交到 HEAD 的改动选择步骤。无法取得基准时执行完整范围。

.PARAMETER ChangedPaths
    与 -List 一起使用，直接查看这些路径会选择哪些步骤。
#>
param(
    [switch]$Fast,
    [switch]$List,
    [string]$ChangedBase,
    [string[]]$ChangedPaths
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$python = if ($IsWindows) { 'py' } else { 'python3' }
$reportDir = Join-Path $root 'test-reports'

# 单位为毫秒。项目在这里修改警告阈值。
$slowTestThresholds = [ordered]@{ Unit = 300; Integration = 5000; E2E = 15000 }

function Step(
    [string]$Name, [string]$Directory, [string]$Command, [string[]]$Arguments,
    [switch]$Fast, [string]$Scope = 'Static', [switch]$RequiredForE2E
) {
    [PSCustomObject]@{
        Name = $Name; Directory = $Directory; Command = $Command; Arguments = $Arguments
        Fast = [bool]$Fast; Scope = $Scope; RequiredForE2E = [bool]$RequiredForE2E
    }
}

function Get-ChangedPaths([string]$Base) {
    Push-Location -LiteralPath $root
    try {
        git cat-file -e "${Base}^{commit}" 2>$null
        if ($LASTEXITCODE -ne 0) {
            git fetch --no-tags --depth=1 origin $Base 2>$null | Out-Null
        }
        $paths = @(git -c core.quotePath=false diff --no-renames --name-only "${Base}..HEAD" -- 2>$null)
        if ($LASTEXITCODE -eq 0) { return ,$paths }
        Write-Warning 'Cannot determine the changed paths. Running full verification.'
        return $null
    }
    finally {
        Pop-Location
    }
}

function Get-Scopes([string[]]$Paths) {
    foreach ($path in $Paths) {
        switch -Wildcard ($path) {
            '*.md' { break }
            'docs/*' { break }
            'backend/*' { 'Backend'; break }
            'frontend/*' { 'Frontend'; break }
            default { return 'All' }
        }
    }
}

function Get-SelectedSteps([bool]$FastMode) {
    $withApplication = $runBackend -or $runFrontend
    $withE2E = $hasE2E -and $withApplication -and -not $FastMode
    foreach ($step in $steps) {
        if ($FastMode -and -not $step.Fast) { continue }
        if ($withE2E -and $step.RequiredForE2E) { $step; continue }
        switch ($step.Scope) {
            'Static' { $step }
            'Backend' { if ($runBackend) { $step } }
            'Frontend' { if ($runFrontend) { $step } }
            'E2E' { if ($withApplication) { $step } }
        }
    }
}

function Show-SlowestTests {
    $rows = foreach ($file in Get-ChildItem -LiteralPath $reportDir -Filter '*.xml') {
        $layer = switch -Wildcard ($file.Name) {
            'unit-*' { 'Unit' }
            'integration-*' { 'Integration' }
            'e2e.xml' { 'E2E' }
        }
        if (-not $layer) { continue }
        $report = [xml](Get-Content -LiteralPath $file.FullName -Raw)
        foreach ($case in $report.SelectNodes('//testcase[@time and not(skipped)]')) {
            $ms = 1000 * [double]::Parse($case.time, [Globalization.CultureInfo]::InvariantCulture)
            [PSCustomObject]@{ Layer = $layer; Milliseconds = $ms; Name = "$($case.classname) :: $($case.name)" }
        }
    }
    foreach ($layer in $slowTestThresholds.Keys) {
        $tests = @($rows | Where-Object Layer -eq $layer)
        if ($tests.Count -eq 0) { continue }
        $threshold = $slowTestThresholds[$layer]
        Write-Host "Slowest $layer tests (warning above $threshold ms):"
        foreach ($test in $tests | Sort-Object Milliseconds -Descending | Select-Object -First 5) {
            $line = '  {0:F1} ms  {1}' -f $test.Milliseconds, $test.Name
            if ($test.Milliseconds -gt $threshold) { Write-Warning $line }
            else { Write-Host $line }
        }
        $slow = @($tests | Where-Object Milliseconds -gt $threshold).Count
        if ($slow -gt 5) { Write-Warning "$slow $layer tests exceeded $threshold ms. See the JUnit reports." }
    }
}

$paths = if ($List -and $PSBoundParameters.ContainsKey('ChangedPaths')) { ,$ChangedPaths }
         elseif ($ChangedBase) { Get-ChangedPaths $ChangedBase }
$scopes = @(Get-Scopes $paths)
$all = $null -eq $paths -or 'All' -in $scopes
$runBackend = $all -or 'Backend' -in $scopes
$runFrontend = $all -or 'Frontend' -in $scopes
$hasE2E = $false
#if (LocalIdentity)
$hasE2E = $true
#endif

$steps = [Collections.Generic.List[object]]::new()
$steps.Add((Step 'check-error-codes' '.' $python @('scripts/check-error-codes.py') -Fast))
#if (IncludeLocalization)
$steps.Add((Step 'check-i18n' '.' $python @('scripts/check-i18n.py') -Fast))
#endif
#if (SpaFrontend && IncludeOperationRecords)
$steps.Add((Step 'check-operation-action-i18n' '.' $python @('scripts/check-operation-action-i18n.py') -Fast))
#endif
$steps.Add((Step 'check-test-diagnostics' '.' $python @('scripts/check-test-diagnostics.py') -Fast))
#if (SpaFrontend)
$steps.Add((Step 'check-setting-group-icons' '.' $python @('scripts/check-setting-group-icons.py') -Fast))
#endif
$steps.Add((Step 'backend-restore' 'backend' 'dotnet' @('restore', 'CompanyName.ProjectName.sln') -Fast -Scope Backend -RequiredForE2E))
$steps.Add((Step 'backend-build' 'backend' 'dotnet' @('build', 'CompanyName.ProjectName.sln', '-c', 'Release', '--no-restore', '-p:TreatWarningsAsErrors=true') -Fast -Scope Backend -RequiredForE2E))
# IDE0001/IDE0002（可简化的全名）构建期不执行，只在这里用官方 dotnet format 核对
$steps.Add((Step 'backend-format' 'backend' 'dotnet' @('format', 'style', 'CompanyName.ProjectName.sln', '--verify-no-changes', '--no-restore', '--diagnostics', 'IDE0001', 'IDE0002') -Fast -Scope Backend))
# 单元测试先于集成测试：新增测试项目自动纳入。
$testProjects = @(
    Get-ChildItem -LiteralPath (Join-Path $root 'backend/tests') -Filter '*.csproj' -Recurse |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
        Sort-Object @{ Expression = { $_.BaseName -notlike '*.UnitTests' } }, BaseName
)
if ($testProjects.Count -eq 0) { throw 'No test project found under backend/tests.' }
foreach ($project in $testProjects) {
    $relative = [IO.Path]::GetRelativePath((Join-Path $root 'backend'), $project.FullName).Replace('\', '/')
    $isUnit = $project.BaseName -like '*.UnitTests'
    $layer = if ($isUnit) { 'unit' } else { 'integration' }
    $report = Join-Path $reportDir "$layer-$($project.BaseName).xml"
    $steps.Add((Step "backend-test:$($project.BaseName)" 'backend' 'dotnet' @('test', $relative, '-c', 'Release', '--no-build', '--logger', "junit;LogFilePath=$report") -Fast:$isUnit -Scope Backend))
}
#if (SpaFrontend)
$steps.Add((Step 'frontend-install' 'frontend' 'npm' @('ci', '--no-audit') -Scope Frontend -RequiredForE2E))
#if (LocalIdentity)
$steps.Add((Step 'check-e2e-admission' 'frontend' 'node' @('scripts/check-e2e-admission.mjs') -Fast -Scope E2E))
#endif
$browserInstall = @('exec', '--', 'playwright', 'install', 'chromium')
if ($env:VERIFY_INSTALL_BROWSER_DEPS -eq '1') { $browserInstall += '--with-deps' }
$steps.Add((Step 'frontend-browser-install' 'frontend' 'npm' $browserInstall -Scope Frontend -RequiredForE2E))
$steps.Add((Step 'frontend-lint' 'frontend' 'npm' @('run', 'lint') -Fast -Scope Frontend))
$steps.Add((Step 'frontend-test' 'frontend' 'npm' @('test', '--', '--watch=false', '--reporters=default', '--reporters=junit', '--output-file=../test-reports/unit-frontend.xml') -Scope Frontend))
$steps.Add((Step 'frontend-build' 'frontend' 'npm' @('run', 'build') -Scope Frontend -RequiredForE2E))
#if (LocalIdentity)
$steps.Add((Step 'frontend-e2e' 'frontend' 'npm' @('run', 'e2e') -Scope E2E))
#endif
#endif

if ($List) {
    foreach ($mode in @('full', 'fast')) {
        foreach ($step in Get-SelectedSteps ($mode -eq 'fast')) {
            "{0}`t{1}`t{2}`t{3} {4}" -f $mode, $step.Name, $step.Directory, $step.Command, ($step.Arguments -join ' ')
        }
    }
    exit 0
}

$started = [Diagnostics.Stopwatch]::StartNew()
if (Test-Path -LiteralPath $reportDir) { Remove-Item -LiteralPath $reportDir -Recurse -Force }
New-Item -ItemType Directory -Path $reportDir | Out-Null
$selected = @(Get-SelectedSteps ([bool]$Fast))
#if (SpaFrontend)
if ($Fast -and ($selected.Directory -contains 'frontend') -and -not (Test-Path -LiteralPath (Join-Path $root 'frontend/node_modules') -PathType Container)) {
    Write-Host "-Fast needs installed frontend dependencies: run 'npm ci' in frontend/ first, or run the full verification." -ForegroundColor Red
    exit 1
}
#endif

$timings = [ordered]@{}
try {
    foreach ($step in $selected) {
        Write-Host "==> [$($step.Name)] $($step.Command) $($step.Arguments -join ' ')" -ForegroundColor Cyan
        $elapsed = [Diagnostics.Stopwatch]::StartNew()
        Push-Location -LiteralPath (Join-Path $root $step.Directory)
        try {
            & $step.Command @($step.Arguments)
            $exitCode = $LASTEXITCODE
        }
        finally {
            Pop-Location
            $timings[$step.Name] = $elapsed.Elapsed
            Write-Host ('<== [{0}] {1:F2} s' -f $step.Name, $elapsed.Elapsed.TotalSeconds)
        }
        if ($exitCode -ne 0) {
            Write-Host "Verification failed at step '$($step.Name)' (exit code $exitCode); remaining steps were not run." -ForegroundColor Red
            exit $exitCode
        }
    }
    Write-Host "Verification passed: $($selected.Count) steps." -ForegroundColor Green
}
finally {
    Write-Host 'Verification timings:'
    foreach ($entry in $timings.GetEnumerator()) {
        Write-Host ('  {0,-40} {1,8:F2} s' -f $entry.Key, $entry.Value.TotalSeconds)
    }
    Write-Host ('  {0,-40} {1,8:F2} s' -f 'Total wall time', $started.Elapsed.TotalSeconds)
    try { Show-SlowestTests }
    catch { Write-Warning "Could not summarize JUnit reports: $_" }
}
