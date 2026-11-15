<#
.SYNOPSIS
  Installs (or updates) the IntelligenceDB Agent as a Windows service.

.DESCRIPTION
  Run in an elevated PowerShell (Run as Administrator) inside the unzipped agent folder.
  - Writes the portal URL, agent id and key to appsettings.json (file access limited to SYSTEM and Administrators).
  - Creates the Windows service "IntelligenceDB Agent" (automatic start) and starts it.
  The agent only makes OUTBOUND HTTPS calls to the portal; no inbound firewall rule is needed.

.EXAMPLE
  .\install-agent.ps1 -PortalUrl "https://intelligencedb.contoso.com" -AgentId "0b6c..." -AgentKey "idbk_..."

.EXAMPLE
  # Run the service as a domain account / gMSA so Windows authentication to SQL Server works:
  .\install-agent.ps1 -PortalUrl ... -AgentId ... -AgentKey ... -ServiceAccount "CONTOSO\gmsa-idbagent$"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $PortalUrl,
    [Parameter(Mandatory)] [string] $AgentId,
    [Parameter(Mandatory)] [string] $AgentKey,
    [string] $InstallDir = "C:\Program Files\IntelligenceDB Agent",
    [string] $ServiceAccount = "",
    [int] $MaxParallelServers = 8
)

$ErrorActionPreference = "Stop"
$serviceName = "IntelligenceDB Agent"

$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Please run this script in PowerShell started with 'Run as Administrator'."
}

if ($PortalUrl -notmatch '^https://') {
    throw "PortalUrl must start with https:// (the portal sends connection secrets to the agent)."
}
[void][Guid]::Parse($AgentId)

$source = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $source "IntelligenceDB.Agent.exe"
if (-not (Test-Path $exe)) { throw "IntelligenceDB.Agent.exe not found next to this script ($source)." }

# Stop an existing installation before copying new files.
$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing -and $existing.Status -ne 'Stopped') {
    Write-Host "Stopping the running agent..."
    Stop-Service -Name $serviceName -Force
    $existing.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
}

Write-Host "Copying files to $InstallDir ..."
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item -Path (Join-Path $source "*") -Destination $InstallDir -Recurse -Force -Exclude "install-agent.ps1", "uninstall-agent.ps1"

$settings = [ordered]@{
    Agent = [ordered]@{
        PortalUrl = $PortalUrl.TrimEnd('/')
        AgentId = $AgentId
        AgentKey = $AgentKey
        MaxParallelServers = $MaxParallelServers
        AllowInsecureHttp = $false
    }
    Logging = @{ LogLevel = @{ Default = "Information"; Microsoft = "Warning"; "System.Net.Http.HttpClient" = "Warning" } }
}
$settingsPath = Join-Path $InstallDir "appsettings.json"
$settings | ConvertTo-Json -Depth 5 | Set-Content -Path $settingsPath -Encoding UTF8

# The key is a secret: only SYSTEM, Administrators (and the service account) may read the settings file.
$acl = New-Object System.Security.AccessControl.FileSecurity
$acl.SetAccessRuleProtection($true, $false)
foreach ($id in @("NT AUTHORITY\SYSTEM", "BUILTIN\Administrators") + @($(if ($ServiceAccount) { $ServiceAccount }))) {
    $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($id, "FullControl", "Allow")))
}
Set-Acl -Path $settingsPath -AclObject $acl

$binPath = "`"" + (Join-Path $InstallDir "IntelligenceDB.Agent.exe") + "`""
if (-not $existing) {
    Write-Host "Creating the Windows service '$serviceName'..."
    New-Service -Name $serviceName -BinaryPathName $binPath -DisplayName $serviceName `
        -Description "Monitors local SQL Servers for the IntelligenceDB portal (outbound HTTPS only)." -StartupType Automatic | Out-Null
    # Restart automatically if it ever stops unexpectedly.
    sc.exe failure "$serviceName" reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null
}

if ($ServiceAccount) {
    Write-Host "Setting the service account to $ServiceAccount ..."
    if ($ServiceAccount.EndsWith('$')) {
        sc.exe config "$serviceName" obj= "$ServiceAccount" password= "" | Out-Null   # gMSA: no password
    } else {
        $cred = Get-Credential -UserName $ServiceAccount -Message "Password for the agent service account"
        sc.exe config "$serviceName" obj= "$ServiceAccount" password= "$($cred.GetNetworkCredential().Password)" | Out-Null
    }
}

Start-Service -Name $serviceName
Write-Host ""
Write-Host "Done. The agent should show as 'Online' in the portal (Settings -> Agents) within a minute." -ForegroundColor Green
Write-Host "Logs: Windows Event Viewer -> Windows Logs -> Application (source '$serviceName')."
