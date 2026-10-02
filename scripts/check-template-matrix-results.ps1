#!/usr/bin/env pwsh
param(
    [Parameter(Mandatory)]
    [string]$ResultsPath,
    # 本次 CI 的档位：收据必须声明同一档位，分片与场景集合按该档位核对
    [Parameter(Mandatory)]
    [ValidateSet("pr", "full")]
    [string]$Tier,
    # 要求登记的容器场景通过容器检查
    [switch]$ContainerSmoke
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'template-matrix-scenarios.ps1')

# 每个分片恰好一份收据：缺片、重复、错档或不认识的分片都失败
$expectedSlices = @($MatrixSlices[$Tier].Keys)
$files = @(Get-ChildItem -LiteralPath $ResultsPath -Filter 'matrix-*.json' -File -Recurse)
if ($files.Count -ne $expectedSlices.Count) {
    throw "Expected $($expectedSlices.Count) $Tier-tier slice receipts, found $($files.Count)."
}
$seenSlices = @()
$seenScenarios = @()
foreach ($file in $files) {
    $receipt = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    if ($receipt.Tier -cne $Tier) {
        throw "Slice receipt declares tier '$($receipt.Tier)', expected '$Tier'."
    }
    if ($receipt.Slice -notin $expectedSlices -or $receipt.Slice -in $seenSlices) {
        throw "Unknown or duplicate $Tier-tier slice receipt: '$($receipt.Slice)'."
    }
    $seenSlices += $receipt.Slice
    $expected = Get-TierScenarios $Tier $receipt.Slice
    $actual = @($receipt.Results | ForEach-Object { $_.Scenario })
    if ($actual.Count -ne $expected.Count -or @($actual | Sort-Object -Unique).Count -ne $actual.Count -or
        @($expected | Where-Object { $_ -notin $actual }).Count -gt 0) {
        throw "Slice '$($receipt.Slice)' scenario coverage differs from its registered set."
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
if (@(Get-TierScenarios $Tier | Where-Object { $_ -notin $seenScenarios }).Count -gt 0 -or
    ($ContainerSmoke -and $ContainerScenario -notin $seenScenarios)) {
    throw "Template matrix ($Tier tier) or requested container coverage is incomplete."
}
Write-Host "Template matrix receipts passed for $($seenScenarios.Count) $Tier-tier scenarios in $($seenSlices.Count) slices." -ForegroundColor Green
