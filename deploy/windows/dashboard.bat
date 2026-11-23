@echo off
title irpotify Server
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0deploy\windows\install.ps1" -Launch
