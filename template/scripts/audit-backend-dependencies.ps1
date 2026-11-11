#!/usr/bin/env pwsh

<#
.SYNOPSIS
审计后端 NuGet 依赖（含传递依赖）的安全公告：有 high/critical 即失败，low/moderate 只列出。

.DESCRIPTION
构建把 NuGet 公告（NU1901–NU1904）留作告警（见 backend/Directory.Build.props）：公告随 nuget.org 收录而变，
与本次改动无关，升为错误会让所有流水线同时变红。公告由 CI 的后端依赖审计把关：定时、手动，以及包版本文件变化时运行。
门槛与前端一致：high/critical 为 0。
dotnet list package --vulnerable 查出漏洞时退出码仍为 0，所以按 JSON 输出的 severity 判定。
本地单独运行：pwsh scripts/audit-backend-dependencies.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$solution = @(Get-ChildItem (Join-Path $root 'backend') -File | Where-Object { $_.Extension -in '.sln', '.slnx' })[0]
if (-not $solution) { throw 'backend 下没有解决方案文件' }

& dotnet restore $solution.FullName
if ($LASTEXITCODE -ne 0) { throw "还原失败（退出码 $LASTEXITCODE）" }

$json = & dotnet list $solution.FullName package --vulnerable --include-transitive --format json 2>&1 | Out-String
if ($LASTEXITCODE -ne 0) { throw "dotnet list package 失败（退出码 $LASTEXITCODE）：`n$json" }

$report = $json | ConvertFrom-Json
# 审计源没有漏洞数据（NU1905）或取不到时命令照样返回 0、漏洞列表为空：空列表证明不了没有公告，有诊断就失败
$problems = @($report.problems | Where-Object { $_ })
if ($problems.Count -gt 0) {
    throw "漏洞数据不完整，不能判定为无公告：`n$(($problems | ForEach-Object { "[$($_.level)] $($_.text)" }) -join "`n")"
}
$blocking = [System.Collections.Generic.List[string]]::new()
$seen = @{}
foreach ($project in @($report.projects)) {
    foreach ($framework in @($project.frameworks)) {
        foreach ($package in @($framework.topLevelPackages) + @($framework.transitivePackages)) {
            foreach ($vulnerability in @($package.vulnerabilities)) {
                if (-not $vulnerability) { continue }
                $line = "$($package.id) $($package.resolvedVersion) [$($vulnerability.severity)] $($vulnerability.advisoryurl)"
                if ($seen.ContainsKey($line)) { continue }
                $seen[$line] = $true
                Write-Host "  $line"
                if ($vulnerability.severity -cin @('High', 'Critical')) { $blocking.Add($line) }
            }
        }
    }
}

Write-Host "已扫描 $($solution.Name)：$($seen.Count) 条公告。"
if ($blocking.Count -gt 0) {
    throw "后端依赖有 high/critical 公告，按 docs/standards/tech-stack.md §3 升级：`n$($blocking -join "`n")"
}
Write-Host '✅ 后端依赖无 high/critical 公告。'
