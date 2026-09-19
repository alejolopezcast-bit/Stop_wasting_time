<#
.SYNOPSIS
    Puts a "Stop Wasting Time" shortcut on the desktop.

.DESCRIPTION
    Points the shortcut at the published executable, so the app can be started without opening a terminal.
    Run scripts/publish.ps1 first. Elevation is not configured here: the app manifest already makes
    Windows ask for administrator rights on every launch.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts/create-shortcut.ps1
#>
[CmdletBinding()]
param(
    [string] $TargetPath,

    [string] $ShortcutDirectory = [Environment]::GetFolderPath('Desktop')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
if (-not $TargetPath) {
    $TargetPath = Join-Path $repositoryRoot 'publish\StopWastingTime.exe'
}

if (-not (Test-Path $TargetPath)) {
    throw "Could not find $TargetPath. Run scripts/publish.ps1 first."
}

$TargetPath = (Resolve-Path $TargetPath).Path
$shortcutPath = Join-Path $ShortcutDirectory 'Stop Wasting Time.lnk'

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $TargetPath
$shortcut.WorkingDirectory = Split-Path -Parent $TargetPath
$shortcut.IconLocation = $TargetPath
$shortcut.Description = 'Focus sessions that block the apps and sites that distract you'
$shortcut.Save()

Write-Host "Shortcut created: $shortcutPath" -ForegroundColor Green
