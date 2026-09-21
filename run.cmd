@echo off
REM Double-click launcher: builds Stop Wasting Time and starts it.
REM Windows asks for administrator rights, which the app needs to block apps and sites.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\run.ps1" %*
