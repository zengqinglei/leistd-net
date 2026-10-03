#!/usr/bin/env pwsh
param(
    [Parameter(Mandatory)]
    [string]$ResultsPath,
    # 本次 CI 的档位：收据必须声明同一档位，分片与场景集合按该档位核对
    [Parameter(Mandatory)]
    [ValidateSet("pr", "full")]
    [string]$Tier,
    [string]$ValidationPlanPath,
    # 要求登记的容器场景通过容器检查
    [switch]$ContainerSmoke
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'template-matrix-scenarios.ps1')
. (Join-Path $PSScriptRoot 'quality-validation-plan.ps1')
$selected = @(Get-TierScenarios $Tier)
$mode = 'full'
if ($ValidationPlanPath) {
    $plan = Read-QualityValidationPlan $ValidationPlanPath $Tier
    if ($plan.DocsOnly) { throw 'Documentation-only has no dynamic receipts.' }
    $selected = @($plan.Scenarios)
    $mode = $plan.Mode
}
if ($ContainerSmoke -and $mode -cne 'full') { throw 'Container checks require full stages.' }

# 每个分片恰好一份收据：缺片、重复、错档或不认识的分片都失败
$expectedSlices = @($MatrixSlices[$Tier].Keys | Where-Object {
    $registeredSlice = $_
    @($selected | Where-Object { $scenarioMap[$_].Slices[$Tier] -ceq $registeredSlice }).Count -gt 0
})
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
    if ($receipt.CandidateSha -cne (git rev-parse HEAD) -or $receipt.Mode -cne $mode) {
        throw 'Receipt candidate SHA or validation mode differs from the expected plan.'
    }
    if ($receipt.Slice -notin $expectedSlices -or $receipt.Slice -in $seenSlices) {
        throw "Unknown or duplicate $Tier-tier slice receipt: '$($receipt.Slice)'."
    }
    $seenSlices += $receipt.Slice
    $expected = @(Get-TierScenarios $Tier $receipt.Slice | Where-Object { $_ -cin $selected })
    $actual = @($receipt.Results | ForEach-Object { $_.Scenario })
    if ($actual.Count -ne $expected.Count -or @($actual | Sort-Object -Unique).Count -ne $actual.Count -or
        @($expected | Where-Object { $_ -notin $actual }).Count -gt 0) {
        throw "Slice '$($receipt.Slice)' scenario coverage differs from its registered set."
    }
    foreach ($result in $receipt.Results) {
        foreach ($stage in @('Backend', 'Runtime', 'Lint', 'Frontend', 'Test')) {
            $notApplicable = ($mode -ceq 'frontend' -and $stage -cin @('Backend', 'Runtime')) -or
                ($mode -ceq 'backend' -and $stage -cin @('Lint', 'Frontend', 'Test'))
            $expectedStage = if ($notApplicable) { 'not-applicable' } else { 'pass' }
            if ($result.$stage -cne $expectedStage) { throw "$($result.Scenario): $stage expected $expectedStage." }
        }
        if ($ContainerSmoke -and $result.Scenario -eq $ContainerScenario -and $result.Container -cne 'pass') {
            throw "$($result.Scenario): required container smoke did not pass."
        }
        $seenScenarios += $result.Scenario
    }
}
if (@($selected | Where-Object { $_ -notin $seenScenarios }).Count -gt 0 -or
    ($ContainerSmoke -and $ContainerScenario -notin $seenScenarios)) {
    throw "Template matrix ($Tier tier) or requested container coverage is incomplete."
}
Write-Host "Template matrix receipts passed for $($seenScenarios.Count) $Tier-tier scenarios in $($seenSlices.Count) slices." -ForegroundColor Green
