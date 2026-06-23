#!/usr/bin/env pwsh
<#
.SYNOPSIS
    将 framework/artifacts 下的 NuGet 包（含符号包）推送到 NuGet 源。

.DESCRIPTION
    API Key 从环境变量 NUGET_API_KEY 读取，脚本不硬编码密钥。
    默认推送到 nuget.org；可用 -Source 指定私有源。
    .snupkg 会随 .nupkg 一并推送（nuget.org 支持符号服务器）。

.PARAMETER Source
    NuGet 源 URL，默认 https://api.nuget.org/v3/index.json。

.PARAMETER ApiKey
    API Key；缺省时读取环境变量 NUGET_API_KEY。

.PARAMETER Artifacts
    包目录，默认 framework/artifacts。

.EXAMPLE
    $env:NUGET_API_KEY = "oy2..."; pwsh framework/build/push.ps1
#>
[CmdletBinding()]
param(
    [string]$Source = "https://api.nuget.org/v3/index.json",
    [string]$ApiKey = $env:NUGET_API_KEY,
    [string]$Artifacts = ""
)

$ErrorActionPreference = "Stop"

$FrameworkRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Artifacts)) {
    $Artifacts = Join-Path $FrameworkRoot "artifacts"
}

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    throw "未提供 API Key。请设置环境变量 NUGET_API_KEY 或传入 -ApiKey。"
}
if (-not (Test-Path $Artifacts)) {
    throw "包目录不存在: $Artifacts，请先运行 pack.ps1。"
}

$packages = Get-ChildItem $Artifacts -Filter *.nupkg
if ($packages.Count -eq 0) { throw "未找到 .nupkg，请先运行 pack.ps1。" }

Write-Host "==> 推送 $($packages.Count) 个包到 $Source" -ForegroundColor Cyan
foreach ($pkg in $packages) {
    Write-Host "    push $($pkg.Name)" -ForegroundColor DarkGray
    # --skip-duplicate 容忍重复版本；nuget push 会自动带上同名 .snupkg
    dotnet nuget push $pkg.FullName --source $Source --api-key $ApiKey --skip-duplicate
    if ($LASTEXITCODE -ne 0) { throw "推送失败: $($pkg.Name) (exit $LASTEXITCODE)" }
}
Write-Host "==> 全部推送完成" -ForegroundColor Green
