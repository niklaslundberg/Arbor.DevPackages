#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Stops and removes the Arbor.DevPackages.Server Windows Service.

.DESCRIPTION
    Windows only. Requires an elevated (Administrator) PowerShell session.

.PARAMETER ServiceName
    Windows Service name. Defaults to "Arbor.DevPackages".

.EXAMPLE
    ./scripts/uninstall-windows-service.ps1
#>
[CmdletBinding()]
param(
    [string]$ServiceName = "Arbor.DevPackages"
)

$ErrorActionPreference = "Stop"

if (-not $IsWindows) {
    throw "uninstall-windows-service.ps1 removes a Windows Service and must be run on Windows."
}

$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script from an elevated (Administrator) PowerShell session."
}

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $service) {
    Write-Host "No service named '$ServiceName' is registered; nothing to do." -ForegroundColor Yellow
    return
}

if ($service.Status -ne "Stopped") {
    Stop-Service -Name $ServiceName -Force
    $service.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(30))
}

# Remove-Service was added in PowerShell 6+; sc.exe is used as a fallback for Windows PowerShell 5.1.
if (Get-Command Remove-Service -ErrorAction SilentlyContinue) {
    Remove-Service -Name $ServiceName
}
else {
    sc.exe delete $ServiceName | Out-Null
}

Write-Host "Service '$ServiceName' removed." -ForegroundColor Green
