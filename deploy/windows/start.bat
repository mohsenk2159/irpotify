@echo off
title irpotify Server - Start
cd /d "%~dp0"
echo Starting irpotify Server (PostgreSQL + API + Worker)...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0deploy\windows\install.ps1" -Start
if %ERRORLEVEL% EQU 0 (
    echo.
    echo Server started successfully. Opening dashboard in browser...
    timeout /t 2 >nul
    start http://localhost:5080
) else (
    pause
)
