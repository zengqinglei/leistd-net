#!/usr/bin/env pwsh
<#
.SYNOPSIS
    将框架版本写入模板的 <LeistdFrameworkVersion>。

.DESCRIPTION
    版本号的【唯一来源】是仓库根 VERSION 文件（发布时按提交由 compute-version.ps1 推算递增）。
    模板生成的项目必须自包含，无法读取仓库根的 VERSION，因此其引用的框架版本
    （template/backend/Directory.Build.props 的 <LeistdFrameworkVersion>）以字面值保存，
    在发布时由流水线通过本脚本回写。

    版本来源优先级：
      1. -Version 参数（发布流水线传入计算出的版本，含预发布后缀）——推荐用法。
      2. 缺省时回退读取仓库根 VERSION 文件（本地手动场景）。

.PARAMETER Version
    要写入模板的版本号（正式 x.y.z 或带后缀的预发布版）。

.PARAMETER Check
    仅校验模板当前值是否等于 -Version（或回退值），不修改文件。不一致退出码 1。

.EXAMPLE
    pwsh scripts/sync-version.ps1 -Version 0.8.0      # 发布时写入指定版本
.EXAMPLE
    pwsh scripts/sync-version.ps1                      # 本地：用 VERSION 文件回退
#>
[CmdletBinding()]
param(
    [string]$Version,
    [switch]$Check
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$VersionFile = Join-Path $RepoRoot "VERSION"
$TemplateFile = Join-Path $RepoRoot "template/backend/Directory.Build.props"

if (-not (Test-Path $TemplateFile)) { throw "找不到模板文件: $TemplateFile" }

# 确定目标版本：优先 -Version，否则回退仓库根 VERSION 文件
if ([string]::IsNullOrWhiteSpace($Version)) {
    if (-not (Test-Path $VersionFile)) { throw "未提供 -Version，且找不到回退源: $VersionFile" }
    $Version = (Get-Content $VersionFile -Raw).Trim()
    Write-Host "未指定 -Version，回退使用 VERSION 文件: $Version" -ForegroundColor DarkYellow
}

# 读取模板当前引用版本
$templateText = Get-Content $TemplateFile -Raw
$tplPattern = '<LeistdFrameworkVersion>\s*([^<]*?)\s*</LeistdFrameworkVersion>'
$tplMatch = [regex]::Match($templateText, $tplPattern)
if (-not $tplMatch.Success) { throw "未能在 $TemplateFile 中找到 <LeistdFrameworkVersion>" }
$templateVersion = $tplMatch.Groups[1].Value.Trim()

Write-Host "目标版本     : $Version" -ForegroundColor Cyan
Write-Host "模板当前版本 : $templateVersion" -ForegroundColor Cyan

if ($Check) {
    if ($Version -eq $templateVersion) {
        Write-Host "✓ 一致" -ForegroundColor Green
        exit 0
    }
    Write-Host "✗ 不一致：运行 'pwsh scripts/sync-version.ps1 -Version $Version' 同步。" -ForegroundColor Red
    exit 1
}

if ($Version -eq $templateVersion) {
    Write-Host "✓ 已一致，无需修改。" -ForegroundColor Green
    exit 0
}

$newText = [regex]::Replace($templateText, $tplPattern, "<LeistdFrameworkVersion>$Version</LeistdFrameworkVersion>")
[System.IO.File]::WriteAllText($TemplateFile, $newText)
Write-Host "✓ 已将模板引用版本 $templateVersion → $Version" -ForegroundColor Green
