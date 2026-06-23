#!/usr/bin/env pwsh
<#
.SYNOPSIS
    将框架版本从唯一源同步到模板。

.DESCRIPTION
    版本的【唯一事实来源】是 framework/common.props 的 <Version>（框架所有 Leistd.* 包的打包版本）。
    模板生成的项目必须自包含，无法物理 import 仓库根文件，因此其引用版本
    （template/backend/Directory.Build.props 的 <LeistdFrameworkVersion>）以字面值保存，
    由本脚本从唯一源单向同步，避免两处手工维护漂移。

    - 默认（同步模式）：读取 framework <Version>，回写到 template 的 <LeistdFrameworkVersion>。
    - -Check（校验模式）：只比较两处是否一致，不一致则以非 0 退出（供发布前检查 / CI 用）。

.PARAMETER Check
    仅校验一致性，不修改文件。不一致时退出码为 1。

.EXAMPLE
    pwsh scripts/sync-version.ps1            # 把框架版本同步到模板
.EXAMPLE
    pwsh scripts/sync-version.ps1 -Check     # 校验两处一致（CI / 发布前）
#>
[CmdletBinding()]
param(
    [switch]$Check
)

$ErrorActionPreference = "Stop"

# 仓库根 = 本脚本所在 scripts/ 的上一级
$RepoRoot = Split-Path -Parent $PSScriptRoot
$SourceFile = Join-Path $RepoRoot "framework/common.props"
$TemplateFile = Join-Path $RepoRoot "template/backend/Directory.Build.props"

if (-not (Test-Path $SourceFile)) { throw "找不到版本源文件: $SourceFile" }
if (-not (Test-Path $TemplateFile)) { throw "找不到模板文件: $TemplateFile" }

# 读取唯一源：framework/common.props 的 <Version>
$sourceText = Get-Content $SourceFile -Raw
$sourceMatch = [regex]::Match($sourceText, '<Version>\s*([^<]+?)\s*</Version>')
if (-not $sourceMatch.Success) { throw "未能在 $SourceFile 中找到 <Version>" }
$version = $sourceMatch.Groups[1].Value.Trim()

# 读取模板当前引用版本
$templateText = Get-Content $TemplateFile -Raw
$tplPattern = '<LeistdFrameworkVersion>\s*([^<]*?)\s*</LeistdFrameworkVersion>'
$tplMatch = [regex]::Match($templateText, $tplPattern)
if (-not $tplMatch.Success) { throw "未能在 $TemplateFile 中找到 <LeistdFrameworkVersion>" }
$templateVersion = $tplMatch.Groups[1].Value.Trim()

Write-Host "框架版本（唯一源）   : $version" -ForegroundColor Cyan
Write-Host "模板引用版本         : $templateVersion" -ForegroundColor Cyan

if ($Check) {
    if ($version -eq $templateVersion) {
        Write-Host "✓ 版本一致" -ForegroundColor Green
        exit 0
    }
    Write-Host "✗ 版本不一致：请运行 'pwsh scripts/sync-version.ps1' 同步。" -ForegroundColor Red
    exit 1
}

if ($version -eq $templateVersion) {
    Write-Host "✓ 已一致，无需修改。" -ForegroundColor Green
    exit 0
}

# 同步：把模板版本回写为框架版本
$newText = [regex]::Replace($templateText, $tplPattern, "<LeistdFrameworkVersion>$version</LeistdFrameworkVersion>")
# 保留原文件编码（UTF-8 无 BOM），不引入额外换行
[System.IO.File]::WriteAllText($TemplateFile, $newText)
Write-Host "✓ 已将模板引用版本 $templateVersion → $version" -ForegroundColor Green
