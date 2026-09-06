#!/usr/bin/env pwsh

<#
.SYNOPSIS
    把 Framework 打包到共享本地 feed（.tmp/local-feed），产出前先清空。

.DESCRIPTION
    直接 `dotnet pack -o .tmp/local-feed` 是**往目录里追加**。组件被删除后，
    它的旧 .nupkg 会一直留在这个持久目录里，于是隔离消费验证消费到一个仓库里
    已经不存在的包——它的传递依赖告警（例如已删组件带进来的漏洞）会被当成当前
    代码的问题去排查。CI 每次跑在全新 runner 上，所以这个坑只在本地踩。

    feed 是构建产物，构建产物应当每次从零重建，而不是累积。因此清空由本脚本
    负责，而不是靠人记得多敲一条删除命令。
#>

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$feedRoot = Join-Path $repoRoot ".tmp/local-feed"

# 只允许动 .tmp 下的路径：$feedRoot 虽然是脚本内写死的，但删除是不可逆操作，
# 断言的成本远低于一次误删
$resolvedFeed = [IO.Path]::GetFullPath($feedRoot)
$tempPrefix = [IO.Path]::GetFullPath((Join-Path $repoRoot ".tmp")).TrimEnd([IO.Path]::DirectorySeparatorChar) +
    [IO.Path]::DirectorySeparatorChar
if (-not $resolvedFeed.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to modify path outside .tmp: $resolvedFeed"
}

if (Test-Path -LiteralPath $resolvedFeed) {
    Remove-Item -LiteralPath $resolvedFeed -Recurse -Force
}
New-Item -ItemType Directory -Path $resolvedFeed -Force | Out-Null

dotnet pack (Join-Path $repoRoot "framework/Leistd.Framework.slnx") -c $Configuration -o $resolvedFeed
if ($LASTEXITCODE -ne 0) {
    throw "dotnet pack 失败（退出码 $LASTEXITCODE）"
}

$count = @(Get-ChildItem -LiteralPath $resolvedFeed -File -Filter "*.nupkg").Count
Write-Host "已打包 $count 个包到 $resolvedFeed" -ForegroundColor Green
