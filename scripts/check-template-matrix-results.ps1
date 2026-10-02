#!/usr/bin/env pwsh
param(
    [Parameter(Mandatory)]
    [string]$ResultsPath,
    # 本次 CI 的档位：收据必须声明同一档位，场景集合按该档位核对
    [Parameter(Mandatory)]
    [ValidateSet("pr", "full")]
    [string]$Tier,
    # 要求登记的容器场景通过容器检查
    [switch]$ContainerSmoke
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'template-matrix-scenarios.ps1')

$files = @(Get-ChildItem -LiteralPath $ResultsPath -Filter 'matrix-shard-*.json' -File -Recurse)
if ($files.Count -ne 2) { throw "Expected two CI shard receipts, found $($files.Count)." }
$tierScenarios = Get-TierScenarios $Tier
$seenShards = @()
$seenScenarios = @()
foreach ($file in $files) {
    $receipt = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    if ($receipt.Tier -cne $Tier) {
        throw "Shard receipt declares tier '$($receipt.Tier)', expected '$Tier'."
    }
    if ($receipt.Shard -notin @(1, 2) -or $receipt.Shard -in $seenShards) {
        throw "Invalid or duplicate shard receipt: $($receipt.Shard)."
    }
    $seenShards += $receipt.Shard
    $expected = Get-TierScenarios $Tier $receipt.Shard
    $actual = @($receipt.Results | ForEach-Object { $_.Scenario })
    if ($actual.Count -ne $expected.Count -or @($actual | Sort-Object -Unique).Count -ne $actual.Count -or
        @($expected | Where-Object { $_ -notin $actual }).Count -gt 0) {
        throw "Shard $($receipt.Shard) scenario coverage differs from its registered $Tier set."
    }
    foreach ($result in $receipt.Results) {
        foreach ($stage in @('Backend', 'Runtime', 'Lint', 'Frontend', 'Test')) {
            if ($result.$stage -cne 'pass') { throw "$($result.Scenario): $stage did not pass." }
        }
        if ($ContainerSmoke -and $result.Scenario -eq $ContainerScenario -and $result.Container -cne 'pass') {
            throw "$($result.Scenario): required container smoke did not pass."
        }
        $seenScenarios += $result.Scenario
    }
}
if (@($tierScenarios | Where-Object { $_ -notin $seenScenarios }).Count -gt 0 -or
    ($ContainerSmoke -and $ContainerScenario -notin $seenScenarios)) {
    throw "Template matrix ($Tier tier) or requested container coverage is incomplete."
}
Write-Host "Template matrix receipts passed for $($seenScenarios.Count) $Tier-tier scenarios in two shards." -ForegroundColor Green
