#!/usr/bin/env pwsh

<#
.SYNOPSIS
审计模板前端的生产依赖：用两套锁文件（默认与本地化）各生成一次依赖最全的形态并运行审计。

.DESCRIPTION
模板锁文件带条件指令，不是合法锁文件，只能审计生成后的结果。条件块只增加依赖（没有 #else/#elif），
所以打开全部条件就得到最大依赖闭包：生成后先核对它的 packages 键集合等于模板锁文件去掉条件指令后的集合，
不等说明出现了这里没打开的新条件，直接失败，不放过未审计的依赖块。

调用生成项目的审计脚本。有修复版本的 high/critical 阻断，暂无修复版本的输出警告。
由 CI 的"前端依赖审计"作业与定时检查调用，也可本地单独运行：pwsh scripts/audit-frontend-dependencies.ps1
#>
[CmdletBinding()]
param(
    [string]$WorkRoot = (Join-Path ([IO.Path]::GetTempPath()) "leistd-frontend-audit-$PID")
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$conditionDirective = '^\s*//#(?:if|endif)\b'
# 条件全开：LocalIdentity（ServiceRole 默认 Identity）与通知、实时；第二套另开本地化
$variants = @(
    @{ Name = 'default'; Lock = 'template/frontend/package-lock.json'; Arguments = @() }
    @{ Name = 'localization'; Lock = 'template/.template.config/localization/frontend/package-lock.json'; Arguments = @('--include-localization') }
)
function Invoke-Checked([string]$Command, [string[]]$Arguments) {
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "命令失败（退出码 $LASTEXITCODE）：$Command $($Arguments -join ' ')" }
}

function Get-PackageKeys([string]$Json) {
    @(($Json | ConvertFrom-Json -AsHashtable).packages.Keys | Sort-Object -CaseSensitive)
}

$hive = Join-Path $WorkRoot 'hive'
New-Item -ItemType Directory -Path $WorkRoot -Force | Out-Null
try {
    Invoke-Checked 'dotnet' @('new', 'install', (Join-Path $repoRoot 'template'), '--debug:custom-hive', $hive, '--force')
    foreach ($variant in $variants) {
        $output = Join-Path $WorkRoot $variant.Name
        Invoke-Checked 'dotnet' (@('new', 'fullstack-app', '-n', 'Audit.App', '-o', $output, '--debug:custom-hive', $hive,
                '--include-notifications', '--include-real-time') + $variant.Arguments)
        $frontend = Join-Path $output 'frontend'

        $source = Get-Content -LiteralPath (Join-Path $repoRoot $variant.Lock) | Where-Object { $_ -notmatch $conditionDirective }
        $expected = Get-PackageKeys ($source -join "`n")
        $actual = Get-PackageKeys (Get-Content -LiteralPath (Join-Path $frontend 'package-lock.json') -Raw)
        $difference = Compare-Object $expected $actual -CaseSensitive
        if ($difference) {
            $missing = @($difference | Where-Object SideIndicator -eq '<=' | ForEach-Object InputObject)
            throw "$($variant.Lock) 有依赖块没进入审计形态（缺 $($missing.Count) 个包，如 $($missing | Select-Object -First 3)）：锁文件出现了新条件，在本脚本的生成参数里打开它。"
        }

        Write-Host "审计 $($variant.Name)（$($actual.Count) 个包，覆盖 $($variant.Lock) 全部依赖块）。"
        Push-Location $frontend
        try { Invoke-Checked 'node' @('scripts/audit-dependencies.mjs') }
        finally { Pop-Location }
    }
}
finally {
    Remove-Item -LiteralPath $WorkRoot -Recurse -Force -ErrorAction SilentlyContinue
}
