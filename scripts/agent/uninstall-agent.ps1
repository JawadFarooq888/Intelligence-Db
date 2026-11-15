<#
.SYNOPSIS
  Stops and removes the IntelligenceDB Agent Windows service (and optionally its files).
#>
[CmdletBinding()]
param(
    [string] $InstallDir = "C:\Program Files\IntelligenceDB Agent",
    [switch] $RemoveFiles
)

$ErrorActionPreference = "Stop"
$serviceName = "IntelligenceDB Agent"

$svc = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($svc) {
    if ($svc.Status -ne 'Stopped') { Stop-Service -Name $serviceName -Force }
    sc.exe delete "$serviceName" | Out-Null
    Write-Host "Service removed."
} else {
    Write-Host "Service '$serviceName' is not installed."
}

if ($RemoveFiles -and (Test-Path $InstallDir)) {
    Remove-Item -Recurse -Force $InstallDir
    Write-Host "Files removed from $InstallDir."
}

Write-Host "Delete or disable the agent in the portal (Settings -> Agents) as well."
