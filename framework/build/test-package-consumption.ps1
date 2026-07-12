#!/usr/bin/env pwsh

param(
    [string]$FeedPath,
    [string[]]$PackageIds = @(),
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$tempRoot = Join-Path $repoRoot ".tmp"
$feedRoot = if ($FeedPath) {
    [IO.Path]::GetFullPath($FeedPath, $repoRoot)
}
else {
    Join-Path $tempRoot "local-feed"
}
$consumerRoot = Join-Path $tempRoot "package-consumer"
$projectsRoot = Join-Path $consumerRoot "projects"
$packagesRoot = Join-Path $consumerRoot "packages"
$nugetConfigPath = Join-Path $consumerRoot "NuGet.Config"

function Assert-TempPath([string]$Path) {
    $resolvedTemp = [IO.Path]::GetFullPath($tempRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $resolvedPath = [IO.Path]::GetFullPath($Path)
    if (-not $resolvedPath.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify path outside .tmp: $resolvedPath"
    }
}

function Reset-Directory([string]$Path) {
    Assert-TempPath $Path
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
    New-Item -ItemType Directory -Path $Path | Out-Null
}

function Invoke-External([string]$Command, [string[]]$Arguments, [string]$WorkingDirectory = $repoRoot) {
    Write-Host "> $Command $($Arguments -join ' ')" -ForegroundColor DarkGray
    Push-Location $WorkingDirectory
    try {
        & $Command @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "Command failed with exit code ${LASTEXITCODE}: $Command $($Arguments -join ' ')"
        }
    }
    finally {
        Pop-Location
    }
}

function Get-PackageMetadata([IO.FileInfo]$PackageFile) {
    $archive = [IO.Compression.ZipFile]::OpenRead($PackageFile.FullName)
    try {
        $nuspecEntry = @($archive.Entries | Where-Object { $_.FullName.EndsWith(".nuspec", [StringComparison]::OrdinalIgnoreCase) })
        if ($nuspecEntry.Count -ne 1) {
            throw "$($PackageFile.Name) must contain exactly one nuspec file."
        }

        $reader = [IO.StreamReader]::new($nuspecEntry[0].Open())
        try {
            [xml]$nuspec = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }

        $metadataNode = $nuspec.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']")
        $id = $metadataNode.SelectSingleNode("*[local-name()='id']")?.InnerText
        $version = $metadataNode.SelectSingleNode("*[local-name()='version']")?.InnerText
        if ([string]::IsNullOrWhiteSpace($id) -or [string]::IsNullOrWhiteSpace($version)) {
            throw "$($PackageFile.Name) has an invalid package id or version."
        }

        $entryNames = @($archive.Entries | ForEach-Object { $_.FullName })
        $assemblies = @($entryNames | Where-Object { $_ -match '^lib/[^/]+/[^/]+\.dll$' })
        if ($assemblies.Count -eq 0) {
            throw "$id $version does not contain a lib assembly."
        }

        foreach ($assembly in $assemblies) {
            $xmlPath = $assembly.Substring(0, $assembly.Length - 4) + ".xml"
            if ($xmlPath -notin $entryNames) {
                throw "$id $version is missing XML documentation for $assembly."
            }
        }

        if (-not ($entryNames | Where-Object { $_ -match '^docs/[^/]+\.md$' })) {
            throw "$id $version does not contain a family document under docs/."
        }
        if ("NuGet.md" -notin $entryNames) {
            throw "$id $version does not contain NuGet.md."
        }

        return [PSCustomObject]@{
            Id = $id
            Version = $version
            File = $PackageFile.FullName
            AssemblyCount = $assemblies.Count
        }
    }
    finally {
        $archive.Dispose()
    }
}

if (-not (Test-Path -LiteralPath $feedRoot -PathType Container)) {
    throw "Package feed does not exist: $feedRoot. Run dotnet pack first."
}

$packageFiles = @(Get-ChildItem -LiteralPath $feedRoot -File -Filter "*.nupkg")
if ($packageFiles.Count -eq 0) {
    throw "No nupkg files were found in $feedRoot. Run dotnet pack first."
}

$packages = @($packageFiles | ForEach-Object { Get-PackageMetadata $_ })
$duplicates = @($packages | Group-Object Id | Where-Object Count -gt 1)
if ($duplicates.Count -gt 0) {
    throw "The feed must contain one version per package id. Duplicates: $($duplicates.Name -join ', ')"
}

if ($PackageIds.Count -gt 0) {
    $requestedIds = @($PackageIds | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
    $missingIds = @($requestedIds | Where-Object { $_ -notin $packages.Id })
    if ($missingIds.Count -gt 0) {
        throw "Requested packages were not found in the feed: $($missingIds -join ', ')"
    }
    $packages = @($packages | Where-Object { $_.Id -in $requestedIds })
}

Reset-Directory $consumerRoot
New-Item -ItemType Directory -Path $projectsRoot, $packagesRoot -Force | Out-Null

$escapedFeedRoot = [Security.SecurityElement]::Escape($feedRoot)
$escapedPackagesRoot = [Security.SecurityElement]::Escape($packagesRoot)
$nugetConfig = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <config>
    <add key="globalPackagesFolder" value="$escapedPackagesRoot" />
  </config>
  <packageSources>
    <clear />
    <add key="local-feed" value="$escapedFeedRoot" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="local-feed">
      <package pattern="Leistd.*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
"@
[IO.File]::WriteAllText($nugetConfigPath, $nugetConfig, [Text.UTF8Encoding]::new($false))

$results = [System.Collections.Generic.List[object]]::new()
foreach ($package in $packages | Sort-Object Id) {
    $projectRoot = Join-Path $projectsRoot $package.Id.ToLowerInvariant()
    New-Item -ItemType Directory -Path $projectRoot -Force | Out-Null
    $projectPath = Join-Path $projectRoot "PackageConsumer.csproj"
    $escapedId = [Security.SecurityElement]::Escape($package.Id)
    $escapedVersion = [Security.SecurityElement]::Escape($package.Version)
    $project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="$escapedId" Version="$escapedVersion" />
  </ItemGroup>
</Project>
"@
    [IO.File]::WriteAllText($projectPath, $project, [Text.UTF8Encoding]::new($false))

    Invoke-External "dotnet" @("restore", $projectPath, "--configfile", $nugetConfigPath) $projectRoot
    Invoke-External "dotnet" @("build", $projectPath, "-c", $Configuration, "--no-restore") $projectRoot

    $results.Add([PSCustomObject]@{
        Package = $package.Id
        Version = $package.Version
        Assemblies = $package.AssemblyCount
        Restore = "pass"
        Build = "pass"
    })
}

$results | Format-Table -AutoSize
Write-Host "Package consumption passed for $($results.Count) package(s)." -ForegroundColor Green
