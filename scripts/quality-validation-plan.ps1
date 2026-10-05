# Shared validation of the independently supplied expected plan. Receipts never
# decide for themselves which scenarios/stages were required.
function Read-QualityValidationPlan([string]$Path, [string]$ExpectedTier) {
    $plan = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    $head = git rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or $plan.Version -ne 1 -or $plan.CandidateSha -cne $head -or
        $plan.Tier -cne $ExpectedTier -or $plan.Mode -cnotin @('full', 'frontend', 'backend') -or
        $plan.DocsOnly -isnot [bool] -or $plan.FrameworkTests -isnot [bool]) {
        throw 'Invalid quality plan: version, candidate, tier or mode differs.'
    }
    $registered = @(Get-TierScenarios $ExpectedTier)
    $selected = @($plan.Scenarios)
    if ($plan.Scenarios -isnot [array] -or ($null -ne $plan.ConsumerProjects -and $plan.ConsumerProjects -isnot [array]) -or
        $selected.Count -ne @($selected | Sort-Object -Unique).Count -or
        @($selected | Where-Object { $_ -cnotin $registered }).Count -gt 0 -or
        (-not $plan.DocsOnly -and $selected.Count -eq 0)) { throw 'Invalid quality plan scenario set.' }
    if (-not $plan.DocsOnly -and $plan.Mode -ceq 'full' -and
        (-not $plan.FrameworkTests -or $selected.Count -ne $registered.Count -or
        ($null -ne $plan.ConsumerProjects -and @($plan.ConsumerProjects).Count -eq 0))) { throw 'Full mode must retain all scenarios and framework tests.' }
    if ($ExpectedTier -ceq 'full' -and ($plan.DocsOnly -or $plan.Mode -cne 'full' -or
        -not $plan.FrameworkTests -or $null -ne $plan.ConsumerProjects -or
        $selected.Count -ne $registered.Count)) { throw 'Full-tier plan may not narrow validation.' }
    if ($plan.Mode -cne 'full' -and ($plan.FrameworkTests -or $null -eq $plan.ConsumerProjects -or
        @($plan.ConsumerProjects).Count -ne 0 -or $plan.DocsOnly)) { throw 'Invalid template-only plan responsibilities.' }
    return $plan
}
