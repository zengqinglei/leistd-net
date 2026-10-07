#!/usr/bin/env pwsh
param(
    [Parameter(Mandatory)][string]$ValidationPlanPath,
    [Parameter(Mandatory)][ValidateSet('package', 'documentation')][string]$Kind,
    [Parameter(Mandatory)][string]$ResultsPath
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'template-matrix-scenarios.ps1')
. (Join-Path $PSScriptRoot 'quality-validation-plan.ps1')
$tier = (Get-Content -LiteralPath $ValidationPlanPath -Raw | ConvertFrom-Json).Tier
$plan = Read-QualityValidationPlan $ValidationPlanPath $tier
$files = @(Get-ChildItem -LiteralPath $ResultsPath -Recurse -File -Filter '*.json')
if ($files.Count -ne 1) { throw 'Expected exactly one responsibility proof.' }
$proof = Get-Content -LiteralPath $files[0].FullName -Raw | ConvertFrom-Json
if ($proof.Version -ne 1 -or $proof.Result -cne 'pass' -or $proof.CandidateSha -cne $plan.CandidateSha) { throw 'Invalid candidate proof.' }
if ($Kind -ceq 'package') {
    $projects = @(git ls-files 'framework/components/*.csproj' 'framework/ddd-struct/*.csproj')
    if (-not $plan.Jobs.'package-consumption' -or $projects.Count -eq 0 -or $proof.Packages -ne $projects.Count -or
        $proof.Documentation -isnot [bool] -or $proof.Documentation -ne $plan.PackageDocumentation -or
        ($proof.Consumers -isnot [long] -and $proof.Consumers -isnot [int]) -or $proof.Consumers -lt 0 -or
        ($null -ne $plan.ConsumerProjects -and @($plan.ConsumerProjects).Count -eq 0 -and $proof.Consumers -ne 0) -or
        ($null -eq $plan.ConsumerProjects -and $proof.Consumers -ne $projects.Count)) { throw 'Package proof responsibility differs.' }
    foreach ($field in @('SnippetBlocks', 'SnippetProjects', 'Counterexamples')) {
        if (($proof.$field -isnot [long] -and $proof.$field -isnot [int]) -or
            ($plan.PackageDocumentation -and $proof.$field -le 0) -or
            (-not $plan.PackageDocumentation -and $proof.$field -ne 0)) { throw "Invalid documentation compilation proof: $field" }
    }
} else {
    if (-not $plan.GeneratedDocumentation -or $proof.EffectiveShapes -ne 320 -or $proof.CiVariants -ne 4 -or $proof.Projects -lt 324) {
        throw 'Generated documentation space is incomplete.'
    }
    foreach ($field in @('MarkdownFiles', 'Skills', 'FrontendMarkdownFiles', 'FormattedContents')) {
        if (($proof.$field -isnot [long] -and $proof.$field -isnot [int]) -or $proof.$field -le 0) { throw "Empty generated documentation proof: $field" }
    }
}
Write-Host "Verified $Kind proof for $($plan.CandidateSha)."
