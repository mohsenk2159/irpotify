@echo off
title irpotify Server - Setup
cd /d "%~dp0"
echo ====================================================
echo        irpotify Windows Server Setup Wizard
echo ====================================================
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0deploy\windows\install.ps1" %*
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo Installation reported an issue.
)
pause
