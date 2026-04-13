@echo off
:: Nome do executável do servidor de SCUM (ajuste se for diferente, ex: SCUMServer.exe)
set EXE_NAME=SCUMServer.exe
:: Caminho completo para o seu arquivo de ligar o servidor
set START_SCRIPT="C:\scumserver\IniciarSCUM.bat"

:LOOP
tasklist /NH /FI "IMAGENAME eq %EXE_NAME%" | find /I "%EXE_NAME%" >nul
if %ERRORLEVEL% neq 0 (
    echo [%date% %time%] O servidor caiu! Reiniciando...
    start "" %START_SCRIPT%
    :: Espera 60 segundos para o servidor abrir antes de verificar de novo
    timeout /t 60 /nobreak
) else (
    echo [%date% %time%] Servidor rodando normalmente.
)

:: Espera 30 segundos antes da próxima verificação
timeout /t 30 /nobreak
goto LOOP