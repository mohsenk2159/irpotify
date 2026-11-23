@echo off
title irpotify Server - Start
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0deploy\windows\install.ps1" -Launch
pause
