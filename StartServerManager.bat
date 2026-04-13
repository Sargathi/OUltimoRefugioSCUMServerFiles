@echo off
title SCUM Server Monitor
:: Este arquivo deve ficar na mesma pasta que o ServerManager.ps1 e o executavel do servidor
powershell -ExecutionPolicy Bypass -File "%~dp0ServerManager.ps1"
pause
