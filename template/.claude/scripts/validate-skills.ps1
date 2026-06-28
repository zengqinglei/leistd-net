param(
    [string]$SkillsRoot = (Join-Path $PSScriptRoot "..\skills")
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path $SkillsRoot).Path
$errors = New-Object System.Collections.Generic.List[string]

function Add-ValidationError([string]$Message) {
    $errors.Add($Message) | Out-Null
}

$skillDirs = Get-ChildItem -LiteralPath $root -Directory
foreach ($dir in $skillDirs) {
    $skillName = $dir.Name
    $skillFile = Join-Path $dir.FullName "SKILL.md"

    if (-not (Test-Path -LiteralPath $skillFile)) {
        Add-ValidationError "[$skillName] missing SKILL.md"
        continue
    }

    $lines = Get-Content -LiteralPath $skillFile -Encoding UTF8
    if ($lines.Count -lt 4 -or $lines[0] -ne "---") {
        Add-ValidationError "[$skillName] SKILL.md must start with YAML frontmatter"
    }
    if (-not ($lines -match "^name:\s+$skillName\s*$")) {
        Add-ValidationError "[$skillName] frontmatter name must match folder name"
    }
    if (-not ($lines -match "^description:\s*")) {
        Add-ValidationError "[$skillName] missing description"
    }

    $lineCount = $lines.Count
    if ($lineCount -gt 500) {
        Add-ValidationError "[$skillName] SKILL.md is too long ($lineCount lines, expected <= 500)"
    }
}

$resourcePattern = '(?<![-A-Za-z0-9_])(templates|references|examples)/[A-Za-z0-9_.-]+'
foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File) {
    $text = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
    $skillDir = $file.Directory
    while ($skillDir -and $skillDir.Parent -and $skillDir.Parent.FullName -ne $root) {
        $skillDir = $skillDir.Parent
    }
    if (-not $skillDir -or $skillDir.Parent.FullName -ne $root) {
        continue
    }

    foreach ($match in [regex]::Matches($text, $resourcePattern)) {
        $ref = $match.Value.TrimEnd([char[]]@(0x60, 0x22, 0x27, 0x2c, 0x2e, 0x29))
        $target = Join-Path $skillDir.FullName $ref
        if (-not (Test-Path -LiteralPath $target)) {
            $relFile = Resolve-Path -LiteralPath $file.FullName -Relative
            Add-ValidationError "[$($skillDir.Name)] missing resource '$ref' referenced by $relFile"
        }
    }
}

$legacyTerms = @(
    "architect-spec",
    "docs/architect-spec.md",
    "skill: `"lint`"",
    "lint Skill",
    "templates/fallback-strategy.md",
    "docs/reviews/"
)

foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File) {
    $text = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
    foreach ($term in $legacyTerms) {
        if ($text.Contains($term)) {
            $relFile = Resolve-Path -LiteralPath $file.FullName -Relative
            Add-ValidationError "legacy term '$term' found in $relFile"
        }
    }
}

if ($errors.Count -gt 0) {
    Write-Host "Skill validation failed:" -ForegroundColor Red
    foreach ($errorItem in $errors) {
        Write-Host " - $errorItem" -ForegroundColor Red
    }
    exit 1
}

Write-Host "Skill validation passed for $($skillDirs.Count) skills." -ForegroundColor Green
