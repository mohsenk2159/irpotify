@echo off
title irpotify Server - Stop
cd /d "%~dp0"
echo Stopping irpotify Server...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0deploy\windows\install.ps1" -Stop
pause
