@echo off
title irpotify Server - Dashboard
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$port = 5080; if (Test-Path 'storage\install.endpoint.json') { try { $cfg = Get-Content 'storage\install.endpoint.json' -Raw | ConvertFrom-Json; if ($cfg.Port) { $port = $cfg.Port } } catch {} }; Start-Process ('http://127.0.0.1:' + $port + '/index.html')"
