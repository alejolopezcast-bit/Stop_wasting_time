<#
.SYNOPSIS
    Builds Stop Wasting Time and launches it.

.DESCRIPTION
    Windows shows a UAC prompt on launch: the app manifest asks for administrator rights, which it needs
    to edit the hosts file and to close programs it did not start.

.PARAMETER Configuration
    Debug (default) or Release.

.PARAMETER NoBuild
    Launch whatever was built last instead of rebuilding.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts/run.ps1
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',

    [switch] $NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
$projectPath = Join-Path $repositoryRoot 'src\StopWastingTime.App\StopWastingTime.App.csproj'

if (-not $NoBuild) {
    Write-Host "Building Stop Wasting Time ($Configuration)..." -ForegroundColor Cyan
    & dotnet build $projectPath -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed with exit code $LASTEXITCODE."
    }
}

$executablePath = Join-Path $repositoryRoot "src\StopWastingTime.App\bin\$Configuration\net10.0-windows\StopWastingTime.exe"
if (-not (Test-Path $executablePath)) {
    throw "Could not find $executablePath. Build the app first, or drop the -NoBuild switch."
}

Write-Host 'Starting Stop Wasting Time. Windows will ask for administrator rights.' -ForegroundColor Cyan
Start-Process -FilePath $executablePath
