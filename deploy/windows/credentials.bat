@echo off
title irpotify Server - Credentials
cd /d "%~dp0"
echo ====================================================
echo           irpotify Server Credentials
echo ====================================================
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0deploy\windows\install.ps1" -Credentials
pause
