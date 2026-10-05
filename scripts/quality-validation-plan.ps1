# Shared validation of the independently supplied expected plan. Receipts never
# decide for themselves which scenarios/stages were required.
function Read-QualityValidationPlan([string]$Path, [string]$ExpectedTier) {
    $plan = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    $head = git rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or $plan.Version -ne 2 -or $plan.CandidateSha -cne $head -or
        $plan.Tier -cne $ExpectedTier -or $plan.Mode -cnotin @('full', 'frontend', 'backend') -or
        $plan.DocsOnly -isnot [bool] -or $plan.FrameworkTests -isnot [bool] -or $plan.ContainerSmoke -isnot [bool]) {
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
    if ($plan.DocsOnly -and ($plan.Mode -cne 'full' -or $selected.Count -ne 0 -or $plan.FrameworkTests -or
        $null -eq $plan.ConsumerProjects -or @($plan.ConsumerProjects).Count -ne 0 -or $plan.ContainerSmoke)) {
        throw 'Invalid documentation-only responsibilities.'
    }
    if ($plan.ContainerSmoke -and $plan.Mode -cne 'full') { throw 'Container checks require full stages.' }
    $logicalGroups = @($MatrixSlices[$ExpectedTier].Keys | Where-Object {
        $logical = $_
        @(Get-TierScenarios $ExpectedTier $logical | Where-Object { $_ -cin $selected }).Count -gt 0
    })
    $count = $logicalGroups.Count
    if ($plan.Slices -isnot [array] -or @($plan.Slices).Count -ne $count) {
        throw 'Invalid quality plan execution group count.'
    }
    $assigned = @()
    for ($index = 0; $index -lt $count; $index++) {
        $group = $plan.Slices[$index]
        if ($group.key -cne ('execution-{0:00}' -f ($index + 1)) -or $group.title -isnot [string] -or
            [string]::IsNullOrWhiteSpace($group.title) -or $group.Scenarios -isnot [array] -or
            @($group.Scenarios).Count -eq 0 -or $group.Containers -isnot [array] -or
            @($group.Scenarios | Where-Object { $_ -cnotin $selected }).Count -gt 0) {
            throw 'Invalid quality plan execution group.'
        }
        $expectedMembers = @(Get-TierScenarios $ExpectedTier $logicalGroups[$index] | Where-Object { $_ -cin $selected })
        if (Compare-Object $expectedMembers @($group.Scenarios) -CaseSensitive -SyncWindow 0) {
            throw 'Quality plan must preserve registered group members and order.'
        }
        $requiredContainers = @($group.Scenarios | Where-Object { $plan.ContainerSmoke -and $_ -cin $ContainerScenarios })
        if (@($group.Containers).Count -ne $requiredContainers.Count -or
            @($group.Containers | Sort-Object -Unique).Count -ne @($group.Containers).Count -or
            @($group.Containers | Where-Object { $_ -cnotin $requiredContainers }).Count -gt 0) {
            throw 'Invalid quality plan container assignment.'
        }
        $assigned += @($group.Scenarios)
    }
    if ($assigned.Count -ne $selected.Count -or @($assigned | Sort-Object -Unique).Count -ne $assigned.Count -or
        @($selected | Where-Object { $_ -cnotin $assigned }).Count -gt 0) {
        throw 'Quality plan must assign every selected scenario exactly once.'
    }
    return $plan
}
