#!/usr/bin/env pwsh
<#
.SYNOPSIS
    打包所有 Leistd.* 框架项目为 NuGet 包（PDB 内嵌）。

.DESCRIPTION
    版本：默认取仓库根 VERSION 文件（common.props 读取它作 VersionPrefix）；发布预发布版时
    由流水线用 dotnet pack -p:Version=... 覆盖。输出到 framework/artifacts/，每个可打包项目
    产出内嵌 PDB 的 .nupkg（无独立 snupkg）。发布流水线见 .github/workflows/release-*.yml。

.PARAMETER Configuration
    构建配置，默认 Release。

.PARAMETER Output
    输出目录，默认 framework/artifacts。

.EXAMPLE
    pwsh framework/build/pack.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Output = ""
)

$ErrorActionPreference = "Stop"

# 脚本所在目录 = framework/build，向上一级为 framework 根，再上一级为仓库根
$FrameworkRoot = Split-Path -Parent $PSScriptRoot
$RepoRoot = Split-Path -Parent $FrameworkRoot
$Solution = Join-Path $FrameworkRoot "Leistd.Framework.slnx"

if ([string]::IsNullOrWhiteSpace($Output)) {
    $Output = Join-Path $FrameworkRoot "artifacts"
}

# 打包前同步版本到模板。本地调用不带 -Version，回退用 common.props 的 VersionPrefix；
# CI 发布流水线会显式传入计算出的版本（见 release-*.yml）。
Write-Host "==> 同步版本到模板" -ForegroundColor Cyan
& (Join-Path $RepoRoot "scripts/sync-version.ps1")
if ($LASTEXITCODE -ne 0) { throw "版本同步失败 (exit $LASTEXITCODE)" }

Write-Host "==> 清理旧产物: $Output" -ForegroundColor Cyan
if (Test-Path $Output) { Remove-Item -Recurse -Force $Output }
New-Item -ItemType Directory -Force -Path $Output | Out-Null

Write-Host "==> dotnet pack ($Configuration) -> $Output" -ForegroundColor Cyan
# PDB 随主 .nupkg 内嵌（由 common.props 的 AllowedOutputExtensionsInPackageBuildOutputFolder 控制），不产独立 snupkg
dotnet pack $Solution -c $Configuration -o $Output /p:ContinuousIntegrationBuild=true
if ($LASTEXITCODE -ne 0) { throw "dotnet pack 失败 (exit $LASTEXITCODE)" }

$nupkgs = Get-ChildItem $Output -Filter *.nupkg
Write-Host ""
Write-Host "==> 完成: $($nupkgs.Count) 个 .nupkg（PDB 已内嵌）" -ForegroundColor Green
$nupkgs | ForEach-Object { Write-Host "    $($_.Name)" }
