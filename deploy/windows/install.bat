@echo off
title irpotify Server - Setup
cd /d "%~dp0"

:: Auto-elevate to Administrator if not already elevated
net session >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo Requesting Administrator privileges...
    powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -ArgumentList '%*' -Verb RunAs"
    exit /b
)

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
