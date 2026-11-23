@echo off
title irpotify Server - Start
cd /d "%~dp0"
echo Starting irpotify Server (PostgreSQL + API + Worker)...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0deploy\windows\install.ps1" -Start
if %ERRORLEVEL% EQU 0 (
    echo.
    echo Server started successfully. Opening dashboard in browser...
    timeout /t 2 >nul
    powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$port = 5080; if (Test-Path 'storage\install.endpoint.json') { try { $cfg = Get-Content 'storage\install.endpoint.json' -Raw | ConvertFrom-Json; if ($cfg.Port) { $port = $cfg.Port } } catch {} }; Start-Process ('http://127.0.0.1:' + $port + '/index.html')"
) else (
    pause
)
