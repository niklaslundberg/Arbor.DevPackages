#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Registers a published, self-contained Arbor.DevPackages.Server build as a Windows Service.

.DESCRIPTION
    Windows only. Requires an elevated (Administrator) PowerShell session.
    Run scripts/publish.ps1 -RuntimeIdentifiers win-x64 first, then point this script at the
    extracted publish output (or the .zip's extracted contents).

.PARAMETER ExecutablePath
    Full path to Arbor.DevPackages.Server.exe from a self-contained win-x64 publish output.

.PARAMETER ServiceName
    Windows Service name. Defaults to "Arbor.DevPackages".

.PARAMETER DisplayName
    Windows Service display name. Defaults to "Arbor.DevPackages NuGet Server".

.PARAMETER StartupType
    Service startup type. Defaults to Automatic.

.EXAMPLE
    ./scripts/install-windows-service.ps1 -ExecutablePath C:\Apps\ArborDevPackages\Arbor.DevPackages.Server.exe
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath,

    [string]$ServiceName = "Arbor.DevPackages",

    [string]$DisplayName = "Arbor.DevPackages NuGet Server",

    [ValidateSet("Automatic", "Manual", "Disabled")]
    [string]$StartupType = "Automatic"
)

$ErrorActionPreference = "Stop"

if (-not $IsWindows) {
    throw "install-windows-service.ps1 registers a Windows Service and must be run on Windows."
}

$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script from an elevated (Administrator) PowerShell session."
}

$ExecutablePath = Resolve-Path $ExecutablePath -ErrorAction Stop

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    throw "A service named '$ServiceName' already exists. Run uninstall-windows-service.ps1 first if you want to replace it."
}

New-Service `
    -Name $ServiceName `
    -BinaryPathName "`"$ExecutablePath`"" `
    -DisplayName $DisplayName `
    -Description "Local NuGet v3 package server (read-through proxy, offline cache)." `
    -StartupType $StartupType

Write-Host "Service '$ServiceName' installed. Start it with: Start-Service -Name '$ServiceName'" -ForegroundColor Green
