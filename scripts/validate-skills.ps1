param(
    [string]$RepositoryRoot = (Join-Path $PSScriptRoot ".."),
    [string]$SkillCreatorValidator = $env:SKILL_CREATOR_VALIDATOR,
    [string]$PythonPath
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path $RepositoryRoot).Path
$errors = [System.Collections.Generic.List[string]]::new()
$skills = [System.Collections.Generic.List[object]]::new()

function Add-ValidationError([string]$Message) {
    $errors.Add($Message)
}

function Get-RepoRelativePath([string]$Path) {
    return [IO.Path]::GetRelativePath($repoRoot, $Path).Replace('\', '/')
}

$skillSets = @(
    @{ Path = ".claude/skills"; Kind = "repository"; Expected = @("developing-leistd-framework", "developing-leistd-template", "maintaining-leistd-repository") },
    @{ Path = "template/.agents/skills"; Kind = "template"; Expected = @("leistd-project-workflow") },
    @{ Path = "skills"; Kind = "distribution"; Expected = @("leistd-net-framework") }
)
$requiredSkillMarkers = @{
    "developing-leistd-framework" = @("test-package-consumption.ps1", "兼容性")
    "maintaining-leistd-repository" = @("单独修改 framework 或 template", "多个交付面")
    "leistd-net-framework" = @("目标包未安装时", "启动真实宿主")
    "leistd-project-workflow" = @("以用户的最终意图决定交付边界", "缺失规范或文档不阻断低风险任务", "references/bootstrap.md")
}

foreach ($set in $skillSets) {
    $skillsRoot = Join-Path $repoRoot $set.Path
    if (-not (Test-Path -LiteralPath $skillsRoot)) {
        Add-ValidationError "Missing skill root: $($set.Path)"
        continue
    }

    $directories = @(Get-ChildItem -LiteralPath $skillsRoot -Directory | Where-Object {
        Test-Path -LiteralPath (Join-Path $_.FullName "SKILL.md")
    })
    $actualNames = @($directories.Name | Sort-Object)
    $difference = Compare-Object ($set.Expected | Sort-Object) $actualNames
    if ($difference) {
        Add-ValidationError "Skill set differs in $($set.Path): $($difference | Out-String)"
    }

    foreach ($directory in $directories) {
        $skillName = $directory.Name
        $skillFile = Join-Path $directory.FullName "SKILL.md"
        $lines = @(Get-Content -LiteralPath $skillFile -Encoding UTF8)
        $text = $lines -join "`n"
        $skills.Add([PSCustomObject]@{
            Name = $skillName
            Kind = $set.Kind
            Directory = $directory.FullName
            File = $skillFile
            Text = $text
        })

        if ($lines.Count -lt 5 -or $lines[0] -ne "---") {
            Add-ValidationError "[$skillName] SKILL.md must start with YAML frontmatter"
            continue
        }

        $closingIndex = -1
        for ($index = 1; $index -lt $lines.Count; $index++) {
            if ($lines[$index] -eq "---") {
                $closingIndex = $index
                break
            }
        }
        if ($closingIndex -lt 3) {
            Add-ValidationError "[$skillName] frontmatter is not closed or is incomplete"
            continue
        }

        $frontmatter = @($lines[1..($closingIndex - 1)])
        $keys = @($frontmatter | ForEach-Object {
            if ($_ -match '^(?<key>[A-Za-z0-9-]+):') { $Matches.key }
        })
        $unexpectedKeys = @($keys | Where-Object { $_ -notin @("name", "description") })
        if ($unexpectedKeys.Count -gt 0) {
            Add-ValidationError "[$skillName] unsupported frontmatter keys: $($unexpectedKeys -join ', ')"
        }
        if (@($keys | Where-Object { $_ -eq "name" }).Count -ne 1 -or @($keys | Where-Object { $_ -eq "description" }).Count -ne 1) {
            Add-ValidationError "[$skillName] frontmatter must contain exactly name and description"
        }

        $nameLine = $frontmatter | Where-Object { $_ -match '^name:' } | Select-Object -First 1
        $declaredName = ($nameLine -replace '^name:\s*', '').Trim('"', "'")
        if ($declaredName -ne $skillName) {
            Add-ValidationError "[$skillName] frontmatter name must match the folder name"
        }
        if ($declaredName.Length -gt 64 -or $declaredName -notmatch '^[a-z0-9]+(?:-[a-z0-9]+)*$') {
            Add-ValidationError "[$skillName] name must be kebab-case and no longer than 64 characters"
        }

        $descriptionLine = $frontmatter | Where-Object { $_ -match '^description:' } | Select-Object -First 1
        $description = ($descriptionLine -replace '^description:\s*', '').Trim('"', "'")
        if ([string]::IsNullOrWhiteSpace($description) -or $description.Length -gt 1024) {
            Add-ValidationError "[$skillName] description must contain 1-1024 characters"
        }
        if ($description -match '[<>]') {
            Add-ValidationError "[$skillName] description must not contain angle brackets"
        }

        $body = if ($closingIndex + 1 -lt $lines.Count) { $lines[($closingIndex + 1)..($lines.Count - 1)] -join "`n" } else { "" }
        if ([string]::IsNullOrWhiteSpace($body)) {
            Add-ValidationError "[$skillName] SKILL.md body is empty"
        }
        if ($lines.Count -gt 500) {
            Add-ValidationError "[$skillName] SKILL.md is too long ($($lines.Count) lines, expected <= 500)"
        }
        foreach ($marker in @($requiredSkillMarkers[$skillName])) {
            if (-not [string]::IsNullOrWhiteSpace($marker) -and -not $text.Contains($marker)) {
                Add-ValidationError "[$skillName] missing required workflow marker: $marker"
            }
        }

        $allowedEntries = @("SKILL.md", "agents", "assets", "references", "scripts")
        foreach ($entry in Get-ChildItem -LiteralPath $directory.FullName -Force) {
            if ($entry.Name -notin $allowedEntries) {
                Add-ValidationError "[$skillName] unexpected skill resource: $($entry.Name)"
            }
        }

        foreach ($match in [regex]::Matches($text, '\]\((?<path>(?:references|scripts|assets)/[^)#\s]+)')) {
            $resourcePath = $match.Groups['path'].Value
            if (-not (Test-Path -LiteralPath (Join-Path $directory.FullName $resourcePath))) {
                Add-ValidationError "[$skillName] missing linked resource: $resourcePath"
            }
        }

        if ($set.Kind -eq "template") {
            if ($lines.Count -gt 120) {
                Add-ValidationError "[$skillName] project workflow SKILL.md must stay concise (expected <= 120 lines)"
            }

            $expectedReferences = @("bootstrap.md", "delivery.md", "development.md", "documentation.md", "quality.md")
            $referencesRoot = Join-Path $directory.FullName "references"
            $actualReferences = if (Test-Path -LiteralPath $referencesRoot) {
                @(Get-ChildItem -LiteralPath $referencesRoot -File -Filter "*.md" | ForEach-Object Name | Sort-Object)
            }
            else {
                @()
            }
            $referenceDifference = Compare-Object ($expectedReferences | Sort-Object) $actualReferences
            if ($referenceDifference) {
                Add-ValidationError "[$skillName] reference set differs: $($referenceDifference | Out-String)"
            }

            foreach ($referenceFile in Get-ChildItem -LiteralPath $referencesRoot -File -Filter "*.md" -ErrorAction SilentlyContinue) {
                if (@(Get-Content -LiteralPath $referenceFile.FullName -Encoding UTF8).Count -gt 100) {
                    Add-ValidationError "[$skillName] reference must stay focused or add a contents section: $($referenceFile.Name)"
                }
            }
        }

        if ($set.Kind -ne "distribution") {
            foreach ($match in [regex]::Matches($text, '`(?<path>(?:docs|framework/docs|scripts|template/docs)/[A-Za-z0-9_./-]+\.(?:md|ps1))`')) {
                $relativePath = $match.Groups['path'].Value
                $basePath = if ($set.Kind -eq "template") { Join-Path $repoRoot "template" } else { $repoRoot }
                if (-not (Test-Path -LiteralPath (Join-Path $basePath $relativePath))) {
                    Add-ValidationError "[$skillName] missing referenced file: $relativePath"
                }
            }
        }
    }
}

$scenarioMapPath = Join-Path $repoRoot "docs/architecture/collaboration-scenarios.md"
if (-not (Test-Path -LiteralPath $scenarioMapPath)) {
    Add-ValidationError "Missing skill scenario map: docs/architecture/collaboration-scenarios.md"
}
else {
    $scenarioMapText = Get-Content -LiteralPath $scenarioMapPath -Raw -Encoding UTF8
    foreach ($marker in @("## 6. Skill 验收", "template/.agents/skills/leistd-project-workflow", "在模板项目中实现订单管理")) {
        if (-not $scenarioMapText.Contains($marker)) {
            Add-ValidationError "Skill scenario map is missing required marker: $marker"
        }
    }
}

$forbiddenPaths = @(
    "template/.claude",
    "template/CLAUDE.md",
    "template/AGENTS.md",
    "template/backend/CLAUDE.md",
    "template/backend/AGENTS.md",
    "template/docs/requirements/req-template-plan.md",
    "template/docs/requirements/registry.md",
    "template/docs/guides",
    "template/docs/modules/_template",
    "template/docs/reports",
    "template/docs/standards/README.md",
    "template/docs/standards/agent-workflow.md",
    "template/docs/standards/api-standard.md",
    "template/docs/standards/code-standard",
    "template/docs/standards/test.md",
    "template/docs/standards/ui-design-strategy.md",
    "template/docs/standards/document-classification.md",
    "template/docs/standards/document-naming.md"
)
foreach ($relativePath in $forbiddenPaths) {
    if (Test-Path -LiteralPath (Join-Path $repoRoot $relativePath)) {
        Add-ValidationError "Removed template mechanism still exists: $relativePath"
    }
}

$validatorCandidates = [System.Collections.Generic.List[string]]::new()
if ($SkillCreatorValidator) {
    $validatorCandidates.Add($SkillCreatorValidator)
}
$validatorCandidates.Add((Join-Path $repoRoot "scripts/vendor/skill-creator/quick_validate.py"))
if ($env:CODEX_HOME) {
    $validatorCandidates.Add((Join-Path $env:CODEX_HOME "skills/.system/skill-creator/scripts/quick_validate.py"))
}
if ($HOME) {
    $validatorCandidates.Add((Join-Path $HOME ".codex/skills/.system/skill-creator/scripts/quick_validate.py"))
}
$validatorPath = $validatorCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

$pythonCandidates = [System.Collections.Generic.List[string]]::new()
if ($PythonPath) {
    $pythonCandidates.Add($PythonPath)
}
foreach ($candidate in @(
    (Join-Path $repoRoot ".tmp/skill-validator-venv/Scripts/python.exe"),
    (Join-Path $repoRoot ".tmp/skill-validator-venv/bin/python")
)) {
    if (Test-Path -LiteralPath $candidate) {
        $pythonCandidates.Add($candidate)
    }
}
foreach ($commandName in @("python", "python3")) {
    $command = Get-Command $commandName -ErrorAction SilentlyContinue
    if ($command) {
        $pythonCandidates.Add($command.Source)
    }
}

$officialValidation = "skipped"
if ($validatorPath) {
    $pythonWithYaml = $pythonCandidates | Where-Object {
        & $_ -c "import yaml" 2>$null
        $LASTEXITCODE -eq 0
    } | Select-Object -First 1

    if ($pythonWithYaml) {
        $previousPythonUtf8 = $env:PYTHONUTF8
        $env:PYTHONUTF8 = "1"
        try {
            foreach ($skill in $skills) {
                & $pythonWithYaml $validatorPath $skill.Directory
                if ($LASTEXITCODE -ne 0) {
                    Add-ValidationError "[$($skill.Name)] official skill-creator validation failed"
                }
            }
            $officialValidation = "passed"
        }
        finally {
            $env:PYTHONUTF8 = $previousPythonUtf8
        }
    }
    else {
        Add-ValidationError "Official skill-creator validator requires a Python environment with PyYAML"
    }
}
else {
    Add-ValidationError "Official skill-creator validator was not found; set SKILL_CREATOR_VALIDATOR"
}

if ($errors.Count -gt 0) {
    Write-Host "Skill validation failed:" -ForegroundColor Red
    foreach ($validationError in $errors) {
        Write-Host " - $validationError" -ForegroundColor Red
    }
    exit 1
}

Write-Host "Skill validation passed for $($skills.Count) skills. Official skill-creator: $officialValidation." -ForegroundColor Green
