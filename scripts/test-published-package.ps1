param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$Source,
    [string]$ArtifactsPath = "framework/artifacts",
    [string]$Username,
    [string]$Token,
    [ValidateRange(0, 60)][int]$MaxWaitMinutes = 30
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$artifactRoot = [IO.Path]::GetFullPath($ArtifactsPath, $repoRoot)
$resolvedSource = if (Test-Path -LiteralPath $Source -PathType Container) { [IO.Path]::GetFullPath($Source, $repoRoot) } else { $Source }
$runRoot = Join-Path $repoRoot (".tmp/published-package/{0}-{1}" -f $PID, (Get-Date -Format "yyyyMMddHHmmssfff"))

if (-not (Test-Path -LiteralPath $artifactRoot -PathType Container)) { throw "Missing artifacts: $artifactRoot" }
$packageFiles = @(Get-ChildItem -LiteralPath $artifactRoot -File -Filter "*.nupkg")
if ($packageFiles.Count -eq 0) { throw "No packages found in $artifactRoot" }
$packageIds = @($packageFiles | ForEach-Object {
    $suffix = ".$Version.nupkg"
    if (-not $_.Name.EndsWith($suffix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Package does not match release version $Version`: $($_.Name)"
    }
    $_.Name.Substring(0, $_.Name.Length - $suffix.Length)
})

function Escape-Xml([string]$Value) { [Security.SecurityElement]::Escape($Value) }

New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
try {
    $references = ($packageIds | ForEach-Object {
        '<PackageReference Include="{0}" Version="{1}" />' -f (Escape-Xml $_), (Escape-Xml $Version)
    }) -join "`n"
    $project = "<Project Sdk=`"Microsoft.NET.Sdk`"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup>$references</ItemGroup></Project>"
    $projectPath = Join-Path $runRoot "PublishedConsumer.csproj"
    [IO.File]::WriteAllText($projectPath, $project)

    $credentials = if ($Token) {
        if (-not $Username) { throw "Username is required when Token is set" }
        '<packageSourceCredentials><target><add key="Username" value="{0}" /><add key="ClearTextPassword" value="{1}" /></target></packageSourceCredentials>' -f (Escape-Xml $Username), (Escape-Xml $Token)
    } else { '' }
    $publicSource = if ($resolvedSource -eq 'https://api.nuget.org/v3/index.json') { '' } else {
        '<add key="nuget.org" value="https://api.nuget.org/v3/index.json" />'
    }
    $publicMapping = if ($publicSource) { '<packageSource key="nuget.org"><package pattern="*" /></packageSource>' } else { '' }
    $targetMapping = if ($publicSource) { '<package pattern="Leistd.*" />' } else { '<package pattern="*" />' }
    $config = '<configuration><packageSources><clear /><add key="target" value="{0}" />{1}</packageSources><packageSourceMapping><packageSource key="target">{2}</packageSource>{3}</packageSourceMapping>{4}</configuration>' -f (Escape-Xml $resolvedSource), $publicSource, $targetMapping, $publicMapping, $credentials
    $configPath = Join-Path $runRoot "NuGet.Config"
    [IO.File]::WriteAllText($configPath, $config)

    $packagesRoot = Join-Path $runRoot "packages"
    $previousPackagesRoot = $env:NUGET_PACKAGES
    $env:NUGET_PACKAGES = $packagesRoot
    try {
        $timer = [Diagnostics.Stopwatch]::StartNew()
        $attempt = 0
        while ($true) {
            $attempt++
            $restoreOutput = (& dotnet restore $projectPath --configfile $configPath --force --no-cache 2>&1) -join "`n"
            Write-Host $restoreOutput
            if ($LASTEXITCODE -eq 0) { break }
            if ($restoreOutput -match '(?i)(\b401\b|\b403\b|unauthorized|forbidden)') {
                throw "Target feed rejected package access; check credentials and permissions"
            }
            $remainingSeconds = $MaxWaitMinutes * 60 - $timer.Elapsed.TotalSeconds
            if ($remainingSeconds -le 0) { throw "Target feed did not restore release $Version within $MaxWaitMinutes minute(s)" }
            $delay = [Math]::Min(60, 10 * [Math]::Pow(2, [Math]::Min($attempt - 1, 3)))
            $delay = [Math]::Min($delay, [Math]::Ceiling($remainingSeconds))
            Write-Host "Release $Version is not restorable yet; retry in $delay second(s) (attempt $attempt)."
            Start-Sleep -Seconds $delay
        }
    }
    finally { $env:NUGET_PACKAGES = $previousPackagesRoot }

    $assets = Get-Content -LiteralPath (Join-Path $runRoot "obj/project.assets.json") -Raw | ConvertFrom-Json -AsHashtable
    foreach ($id in $packageIds) {
        if (-not $assets.libraries.ContainsKey("$id/$Version")) { throw "Target feed did not resolve $id $Version" }
    }
    Write-Host "Target feed restored $($packageIds.Count) Leistd packages at $Version."
}
finally {
    if (Test-Path -LiteralPath $runRoot) { Remove-Item -LiteralPath $runRoot -Recurse -Force }
}
