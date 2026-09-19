<#
.SYNOPSIS
    Publishes Stop Wasting Time as a single self-contained executable.

.DESCRIPTION
    Produces publish\StopWastingTime.exe. By default it is self contained, so it runs on any Windows x64
    machine without installing the .NET runtime, at the cost of a large file (~130 MB). Pass
    -FrameworkDependent for a small executable that needs the .NET 10 Desktop runtime installed.
    Use scripts/create-shortcut.ps1 afterwards to get a desktop shortcut for it.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts/publish.ps1
#>
[CmdletBinding()]
param(
    [string] $OutputDirectory,

    [ValidateSet('win-x64', 'win-arm64')]
    [string] $Runtime = 'win-x64',

    [switch] $FrameworkDependent
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repositoryRoot 'publish'
}

$projectPath = Join-Path $repositoryRoot 'src\StopWastingTime.App\StopWastingTime.App.csproj'

$selfContained = -not $FrameworkDependent
Write-Host "Publishing Stop Wasting Time for $Runtime (self contained: $selfContained)..." -ForegroundColor Cyan
& dotnet publish $projectPath `
    --configuration Release `
    --runtime $Runtime `
    --self-contained $selfContained.ToString().ToLowerInvariant() `
    -p:PublishSingleFile=true `
    --output $OutputDirectory `
    --nologo

if ($LASTEXITCODE -ne 0) {
    throw "Publish failed with exit code $LASTEXITCODE."
}

$executablePath = Join-Path $OutputDirectory 'StopWastingTime.exe'
$sizeInMb = [Math]::Round((Get-Item $executablePath).Length / 1MB, 1)
Write-Host "Done: $executablePath ($sizeInMb MB)" -ForegroundColor Green
