#!/usr/bin/env pwsh
<#
.SYNOPSIS
    打包所有 Leistd.* 框架项目为 NuGet 包（含 snupkg 符号包）。

.DESCRIPTION
    版本由 framework/common.props 的 <Version> 单点控制。
    输出到 framework/artifacts/，每个可打包项目产出 .nupkg + .snupkg。

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

# 脚本所在目录 = framework/build，向上一级为 framework 根
$FrameworkRoot = Split-Path -Parent $PSScriptRoot
$Solution = Join-Path $FrameworkRoot "leistd-framework.slnx"

if ([string]::IsNullOrWhiteSpace($Output)) {
    $Output = Join-Path $FrameworkRoot "artifacts"
}

Write-Host "==> 清理旧产物: $Output" -ForegroundColor Cyan
if (Test-Path $Output) { Remove-Item -Recurse -Force $Output }
New-Item -ItemType Directory -Force -Path $Output | Out-Null

Write-Host "==> dotnet pack ($Configuration) -> $Output" -ForegroundColor Cyan
# snupkg 由 common.props 的 IncludeSymbols/SymbolPackageFormat 控制，无需在此重复传参
dotnet pack $Solution -c $Configuration -o $Output /p:ContinuousIntegrationBuild=true
if ($LASTEXITCODE -ne 0) { throw "dotnet pack 失败 (exit $LASTEXITCODE)" }

$nupkgs = Get-ChildItem $Output -Filter *.nupkg
$snupkgs = Get-ChildItem $Output -Filter *.snupkg
Write-Host ""
Write-Host "==> 完成: $($nupkgs.Count) 个 .nupkg, $($snupkgs.Count) 个 .snupkg" -ForegroundColor Green
$nupkgs | ForEach-Object { Write-Host "    $($_.Name)" }
