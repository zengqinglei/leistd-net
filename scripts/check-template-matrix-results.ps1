#!/usr/bin/env pwsh
param(
    [Parameter(Mandatory)]
    [string]$ResultsPath,
    [string[]]$ContainerSmokeScenarios = @()
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'template-matrix-scenarios.ps1')

$files = @(Get-ChildItem -LiteralPath $ResultsPath -Filter 'matrix-shard-*.json' -File -Recurse)
if ($files.Count -ne 2) { throw "Expected two CI shard receipts, found $($files.Count)." }
$seenShards = @()
$seenScenarios = @()
foreach ($file in $files) {
    $receipt = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    if ($receipt.Shard -notin @(1, 2) -or $receipt.Shard -in $seenShards) {
        throw "Invalid or duplicate shard receipt: $($receipt.Shard)."
    }
    $seenShards += $receipt.Shard
    $expected = @($AllScenarios | Where-Object { $scenarioMap[$_].Shard -eq $receipt.Shard })
    $actual = @($receipt.Results | ForEach-Object { $_.Scenario })
    if ($actual.Count -ne $expected.Count -or @($actual | Sort-Object -Unique).Count -ne $actual.Count -or
        @($expected | Where-Object { $_ -notin $actual }).Count -gt 0) {
        throw "Shard $($receipt.Shard) scenario coverage differs from its registered set."
    }
    foreach ($result in $receipt.Results) {
        foreach ($stage in @('Backend', 'Runtime', 'Lint', 'Frontend', 'Test')) {
            if ($result.$stage -cne 'pass') { throw "$($result.Scenario): $stage did not pass." }
        }
        if ($result.Scenario -in $ContainerSmokeScenarios -and $result.Container -cne 'pass') {
            throw "$($result.Scenario): required container smoke did not pass."
        }
        $seenScenarios += $result.Scenario
    }
}
if (@($AllScenarios | Where-Object { $_ -notin $seenScenarios }).Count -gt 0 -or
    @($ContainerSmokeScenarios | Where-Object { $_ -notin $AllScenarios }).Count -gt 0) {
    throw 'Template matrix or requested container coverage is incomplete.'
}
Write-Host "Template matrix receipts passed for $($seenScenarios.Count) scenarios in two shards." -ForegroundColor Green
