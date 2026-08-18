#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Builds framework-independent (self-contained), single-file release artifacts
    for Arbor.DevPackages.Server, ready for production deployment (including as a
    Windows Service or a Linux systemd service).

.DESCRIPTION
    For each requested runtime identifier, runs `dotnet publish` with:
      - --self-contained true      (no .NET runtime required on the target machine)
      - -p:PublishSingleFile=true  (one executable per platform)
      - -p:PublishTrimmed=false    (ASP.NET Core + reflection-based NuGet libraries
                                    are not trim-safe; trimming is intentionally left off)
    Each publish output is zipped and a SHA-256 checksum file is written alongside it,
    per the release-integrity practice documented in docs/security-review.md.

.PARAMETER Configuration
    Build configuration. Defaults to Release.

.PARAMETER RuntimeIdentifiers
    One or more .NET runtime identifiers to publish for.
    Defaults to win-x64, linux-x64, linux-arm64, osx-x64, osx-arm64.

.PARAMETER Version
    Version to stamp into the assemblies and artifact file names.
    Defaults to `git describe --tags --always`, falling back to "0.0.0-local".

.PARAMETER OutputRoot
    Directory the versioned, zipped artifacts are written to. Defaults to "artifacts/release".

.EXAMPLE
    ./scripts/publish.ps1

.EXAMPLE
    ./scripts/publish.ps1 -RuntimeIdentifiers win-x64 -Version 1.2.3
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",

    [string[]]$RuntimeIdentifiers = @("win-x64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64"),

    [string]$Version,

    [string]$OutputRoot = "artifacts/release"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectPath = Join-Path $repoRoot "src/Arbor.DevPackages.Server/Arbor.DevPackages.Server.csproj"

if (-not $Version) {
    $Version = (git -C $repoRoot describe --tags --always 2>$null)
    if (-not $Version) {
        $Version = "0.0.0-local"
    }
}
$Version = $Version.TrimStart("v")

$outputRootPath = Join-Path $repoRoot $OutputRoot
New-Item -ItemType Directory -Force -Path $outputRootPath | Out-Null

Write-Host "Publishing Arbor.DevPackages.Server $Version ($Configuration) for: $($RuntimeIdentifiers -join ', ')" -ForegroundColor Cyan

$artifacts = @()

foreach ($rid in $RuntimeIdentifiers) {
    $publishDir = Join-Path $outputRootPath "publish/$rid"
    if (Test-Path $publishDir) {
        Remove-Item -Recurse -Force $publishDir
    }

    Write-Host "==> Publishing $rid" -ForegroundColor Yellow

    dotnet publish $projectPath `
        --configuration $Configuration `
        --runtime $rid `
        --self-contained true `
        --output $publishDir `
        -p:Version=$Version `
        -p:PublishSingleFile=true `
        -p:PublishTrimmed=false `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for runtime '$rid' with exit code $LASTEXITCODE."
    }

    $archiveName = "Arbor.DevPackages.Server-$Version-$rid.zip"
    $archivePath = Join-Path $outputRootPath $archiveName
    if (Test-Path $archivePath) {
        Remove-Item -Force $archivePath
    }

    Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $archivePath

    $hash = (Get-FileHash -Path $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $archiveName" | Out-File -FilePath "$archivePath.sha256" -Encoding ascii -NoNewline

    Write-Host "    $archiveName" -ForegroundColor Green
    Write-Host "    sha256: $hash"

    $artifacts += [pscustomobject]@{
        Runtime  = $rid
        Archive  = $archiveName
        Sha256   = $hash
    }
}

$manifestPath = Join-Path $outputRootPath "manifest.json"
[pscustomobject]@{
    Version   = $Version
    BuiltAtUtc = (Get-Date).ToUniversalTime().ToString("O")
    Artifacts = $artifacts
} | ConvertTo-Json -Depth 4 | Out-File -FilePath $manifestPath -Encoding utf8

Write-Host ""
Write-Host "Done. Artifacts written to $outputRootPath" -ForegroundColor Cyan
