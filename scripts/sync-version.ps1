#!/usr/bin/env pwsh
<#
.SYNOPSIS
    将框架版本写入模板的 <LeistdFrameworkVersion>。

.DESCRIPTION
    版本号的【唯一来源】是仓库根 GitVersion.yml（由 GitVersion 从 git tag/历史/提交信息推算）。
    模板生成的项目必须自包含，无法在构建时跑 GitVersion，因此其引用的框架版本
    （template/backend/Directory.Build.props 的 <LeistdFrameworkVersion>）以字面值保存，
    在【正式发布时】由流水线把 GitVersion 算出的版本通过本脚本回写。

    版本来源优先级：
      1. -Version 参数（发布流水线传入 GitVersion 结果）——推荐用法。
      2. 缺省时回退读取 framework/common.props 的 <VersionPrefix>（本地手动场景）。

.PARAMETER Version
    要写入模板的版本号。流水线应传入 GitVersion 的 MajorMinorPatch（正式版）或完整 SemVer。

.PARAMETER Check
    仅校验模板当前值是否等于 -Version（或回退值），不修改文件。不一致退出码 1。

.EXAMPLE
    pwsh scripts/sync-version.ps1 -Version 0.8.0      # 发布时写入正式版本
.EXAMPLE
    pwsh scripts/sync-version.ps1                      # 本地：用 common.props 的 VersionPrefix 回退
#>
[CmdletBinding()]
param(
    [string]$Version,
    [switch]$Check
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$CommonProps = Join-Path $RepoRoot "framework/common.props"
$TemplateFile = Join-Path $RepoRoot "template/backend/Directory.Build.props"

if (-not (Test-Path $TemplateFile)) { throw "找不到模板文件: $TemplateFile" }

# 确定目标版本：优先 -Version，否则回退 common.props 的 <VersionPrefix>
if ([string]::IsNullOrWhiteSpace($Version)) {
    if (-not (Test-Path $CommonProps)) { throw "未提供 -Version，且找不到回退源: $CommonProps" }
    $cp = Get-Content $CommonProps -Raw
    $m = [regex]::Match($cp, '<VersionPrefix>\s*([^<]+?)\s*</VersionPrefix>')
    if (-not $m.Success) { throw "未提供 -Version，且 $CommonProps 中无 <VersionPrefix> 可回退" }
    $Version = $m.Groups[1].Value.Trim()
    Write-Host "未指定 -Version，回退使用 common.props 的 VersionPrefix: $Version" -ForegroundColor DarkYellow
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
