#!/usr/bin/env pwsh
<#
.SYNOPSIS
    按 Conventional Commits 从 VERSION 基准推算下一个版本号（无外部工具，纯 git + PowerShell）。

.DESCRIPTION
    版本基准来自仓库根 VERSION 文件（x.y.z）。本脚本分析"自上个版本 tag 以来"的提交信息，
    决定递增类型并算出新版本，输出供发布流水线使用：

      - BREAKING CHANGE / 类型后带 ! （feat!: / fix!: 等） → major
      - feat: / feat(scope): → minor
      - 其它（fix:/chore:/...） → patch（默认，"优先最小版本"）

    可选给版本追加预发布后缀（beta / nightly）。脚本只计算与输出，不修改文件、不打 tag。

.PARAMETER PreRelease
    预发布通道：'beta' 或 'nightly'。
      beta    → x.y.z-beta.<距上个 tag 的提交数>
      nightly → x.y.z-preview.<yyyyMMdd>
    省略则输出正式版 x.y.z。

.PARAMETER BumpOverride
    手动指定递增类型（major/minor/patch），跳过提交分析。

.OUTPUTS
    向 stdout 打印新版本号；在 GitHub Actions 中同时写入 $GITHUB_OUTPUT 的 version / bump。

.EXAMPLE
    pwsh scripts/compute-version.ps1                 # 正式版：基于提交推算
.EXAMPLE
    pwsh scripts/compute-version.ps1 -PreRelease beta
.EXAMPLE
    pwsh scripts/compute-version.ps1 -PreRelease nightly
#>
[CmdletBinding()]
param(
    [ValidateSet('beta', 'nightly')]
    [string]$PreRelease,

    [ValidateSet('major', 'minor', 'patch')]
    [string]$BumpOverride
)

$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$VersionFile = Join-Path $RepoRoot 'VERSION'
if (-not (Test-Path $VersionFile)) { throw "找不到 VERSION 文件: $VersionFile" }

$base = (Get-Content $VersionFile -Raw).Trim()
if ($base -notmatch '^\d+\.\d+\.\d+$') { throw "VERSION 内容非法（应为 x.y.z）: '$base'" }
$parts = $base.Split('.')
[int]$major = $parts[0]; [int]$minor = $parts[1]; [int]$patch = $parts[2]

# 找上一个版本 tag（v*），据此圈定要分析的提交范围
$lastTag = (git describe --tags --abbrev=0 --match 'v*' 2>$null)
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($lastTag)) {
    $lastTag = $null
    $range = 'HEAD'
    $commitCount = (git rev-list --count HEAD 2>$null)
} else {
    $range = "$lastTag..HEAD"
    $commitCount = (git rev-list --count "$range" 2>$null)
}
if ([string]::IsNullOrWhiteSpace($commitCount)) { $commitCount = '0' }

# 决定递增类型
if ($BumpOverride) {
    $bump = $BumpOverride
} else {
    $commits = if ($lastTag) { git log "$range" --pretty=format:'%s%n%b' --no-merges 2>$null }
               else { git log --pretty=format:'%s%n%b' --no-merges 2>$null }
    $commits = ($commits -join "`n")

    $bump = 'patch'   # 默认：优先最小版本
    if ($commits -match '(?im)(BREAKING CHANGE)|(^[a-z]+(\([^)]*\))?!:)') {
        $bump = 'major'
    } elseif ($commits -match '(?im)^feat(\([^)]*\))?:') {
        $bump = 'minor'
    }
}

switch ($bump) {
    'major' { $major++; $minor = 0; $patch = 0 }
    'minor' { $minor++; $patch = 0 }
    'patch' { $patch++ }
}
$next = "$major.$minor.$patch"

# 预发布后缀（点分数字，保证 NuGet 数值排序）
switch ($PreRelease) {
    'beta'    { $version = "$next-beta.$commitCount" }
    'nightly' { $version = "$next-preview.$(Get-Date -Format 'yyyyMMdd')" }
    default   { $version = $next }
}

Write-Host "上个 tag    : $($lastTag ?? '(无)')"
Write-Host "VERSION 基准: $base"
Write-Host "递增类型    : $bump"
Write-Host "计算版本    : $version"

# 输出（GitHub Actions 友好）
if ($env:GITHUB_OUTPUT) {
    "version=$version" | Out-File -FilePath $env:GITHUB_OUTPUT -Append
    "bump=$bump"       | Out-File -FilePath $env:GITHUB_OUTPUT -Append
    "base=$next"       | Out-File -FilePath $env:GITHUB_OUTPUT -Append
}
# 纯净版本号也打到 stdout 末行，便于脚本捕获
Write-Output $version
