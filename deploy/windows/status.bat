@echo off
title irpotify Server - Status
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0deploy\windows\install.ps1" -Status
pause
