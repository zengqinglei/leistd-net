#!/usr/bin/env pwsh

<#
.SYNOPSIS
    i18n 词条一致性闸门。

.DESCRIPTION
    两段判据，各自只有一份实现：
      1. template/scripts/check-i18n.py —— 随模板分发、生成项目里同样可跑的项目判据：
         前端词条、静态引用与路由 scope 登记，后端资源、模板错误码与 DataAnnotations 键，写死的中文。
      2. scripts/check-i18n-repo.py —— 只在本仓成立的判据：框架组件译文与错误码、宿主不复制组件译文、
         不启用多语言时的英文表（两个分支只在模板源码里同时存在）。
    判据清单见两个脚本的文件头。退出码非 0 表示存在不一致，供 CI 阻断。

.PARAMETER SelfTest
    运行两段判据的正反例自检。
#>
[CmdletBinding()]
param(
    [switch]$SelfTest
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$python = (Get-Command python3 -ErrorAction SilentlyContinue).Source
if (-not $python) { $python = (Get-Command python -ErrorAction SilentlyContinue).Source }
if (-not $python -or (& $python --version 2>&1) -notmatch '^Python 3\.') {
    throw "未找到 Python 3 解释器（python3/python）。"
}

$checks = @(
    (Join-Path $repoRoot "template/scripts/check-i18n.py"),
    (Join-Path $PSScriptRoot "check-i18n-repo.py")
)
$failed = 0
foreach ($check in $checks) {
    if ($SelfTest) { & $python $check --self-test } else { & $python $check }
    if ($LASTEXITCODE -ne 0) { $failed++ }
}
exit $failed
