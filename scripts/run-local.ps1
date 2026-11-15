<#
.SYNOPSIS
  Runs IntelligenceDB on this Windows computer (API + Worker + web) for a demo or local use.

.DESCRIPTION
  Needs: .NET 10 SDK, Node.js 20+ and a SQL Server for the repository database
  (IntelligenceDB creates the database IntelligenceDB_Repo on it).
  Opens three windows (API, Worker, Web) and then the browser at http://localhost:5173.
  Close those windows to stop IntelligenceDB.

.EXAMPLE
  # SQL Server on this computer, Windows login:
  powershell -ExecutionPolicy Bypass -File scripts\run-local.ps1

.EXAMPLE
  # Named instance / other server, Windows login:
  powershell -ExecutionPolicy Bypass -File scripts\run-local.ps1 -SqlServer "OFFICE-SQL01\SQLEXPRESS"

.EXAMPLE
  # SQL login:
  powershell -ExecutionPolicy Bypass -File scripts\run-local.ps1 -SqlServer "10.0.0.5" -SqlUser "sa" -SqlPassword "..."
#>
[CmdletBinding()]
param(
    [string] $SqlServer = "localhost",
    [string] $SqlUser = "",
    [string] $SqlPassword = "",
    [string] $AdminEmail = "admin@intelligencedb.local",
    [string] $AdminPassword = "Admin!Dev2026pass"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $root

function Need($cmd, $name, $url) {
    if (-not (Get-Command $cmd -ErrorAction SilentlyContinue)) {
        Write-Host "$name is not installed. Download it from $url, install it, then open a NEW terminal and run this script again." -ForegroundColor Red
        exit 1
    }
}
Need "dotnet" ".NET 10 SDK" "https://dotnet.microsoft.com/download/dotnet/10.0"
Need "node" "Node.js" "https://nodejs.org"

$sdks = (& dotnet --list-sdks) -join "`n"
if ($sdks -notmatch '(?m)^10\.') {
    Write-Host ".NET 10 SDK is required (found: $sdks). Install it from https://dotnet.microsoft.com/download/dotnet/10.0" -ForegroundColor Red
    exit 1
}

# Repository database connection (database is created automatically on first start).
$auth = if ($SqlUser) { "User Id=$SqlUser;Password=$SqlPassword" } else { "Integrated Security=True" }
$repo = "Server=$SqlServer;Database=IntelligenceDB_Repo;$auth;Encrypt=True;TrustServerCertificate=True"

# A signing key that stays the same between runs (kept in a local, git-ignored file).
$keyFile = Join-Path $root ".local-jwt-key"
if (-not (Test-Path $keyFile)) {
    $bytes = New-Object byte[] 48
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    [Convert]::ToBase64String($bytes) | Set-Content -Path $keyFile -NoNewline
}

# These environment variables are inherited by the three windows started below.
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:DOTNET_ENVIRONMENT = "Development"
$env:ConnectionStrings__Repository = $repo
$env:Jwt__SigningKey = (Get-Content $keyFile -Raw)
$env:Bootstrap__AdminEmail = $AdminEmail
$env:Bootstrap__AdminPassword = $AdminPassword

Write-Host "==> Building (first time takes a few minutes: packages are downloaded)..." -ForegroundColor Cyan
dotnet build IntelligenceDB.slnx -v q
if ($LASTEXITCODE -ne 0) { Write-Host "Build failed - see the messages above." -ForegroundColor Red; exit 1 }

$web = Join-Path $root "src\IntelligenceDB.Web"
if (-not (Test-Path (Join-Path $web "node_modules"))) {
    Write-Host "==> Installing web packages..." -ForegroundColor Cyan
    Push-Location $web
    npm ci
    if ($LASTEXITCODE -ne 0) { Pop-Location; Write-Host "npm install failed." -ForegroundColor Red; exit 1 }
    Pop-Location
}

Write-Host "==> Starting the API (it creates the IntelligenceDB_Repo database on $SqlServer)..." -ForegroundColor Cyan
Start-Process powershell -ArgumentList "-NoExit", "-Command", "`$host.UI.RawUI.WindowTitle='IntelligenceDB API'; dotnet run --project src\IntelligenceDB.Api --launch-profile http --no-build" -WorkingDirectory $root

$healthy = $false
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Seconds 3
    try { if ((Invoke-WebRequest "http://localhost:5080/health" -UseBasicParsing -TimeoutSec 3).StatusCode -eq 200) { $healthy = $true; break } } catch { }
}
if (-not $healthy) {
    Write-Host "The API did not start. Look at the 'IntelligenceDB API' window: usually the SQL Server name or login is wrong." -ForegroundColor Red
    exit 1
}

Write-Host "==> Starting the Worker (collector) and the web app..." -ForegroundColor Cyan
Start-Process powershell -ArgumentList "-NoExit", "-Command", "`$host.UI.RawUI.WindowTitle='IntelligenceDB Worker'; dotnet run --project src\IntelligenceDB.Worker --no-build" -WorkingDirectory $root
Start-Process powershell -ArgumentList "-NoExit", "-Command", "`$host.UI.RawUI.WindowTitle='IntelligenceDB Web'; npm run dev" -WorkingDirectory $web

Start-Sleep -Seconds 8
Start-Process "http://localhost:5173"

Write-Host ""
Write-Host "IntelligenceDB is running:  http://localhost:5173" -ForegroundColor Green
Write-Host "Sign in with:  $AdminEmail  /  $AdminPassword"
Write-Host "Add office SQL Servers with Servers -> Add server (Connect via: Directly from the portal)."
Write-Host "To stop: close the three IntelligenceDB windows."
