# Configuração paths
$ServerExe = "SCUMServer" # Nome do processo sem .exe
$ServerRoot = "C:\scumserver" # Raiz onde ficam as pastas Saved e backup
$StartScript = Join-Path $ServerRoot "IniciarSCUM.bat"
$SavedDir = Join-Path $ServerRoot "SCUM\Saved\SaveFiles" # Pasta onde fica o SCUM.db
$BackupDir = Join-Path $ServerRoot "backup"

if (!(Test-Path $BackupDir)) { New-Item -ItemType Directory -Path $BackupDir }

function Create-Backup {
    $Timestamp = Get-Date -Format "yyyy-MM-dd_HH-mm"
    $BackupFileDB = Join-Path $BackupDir "SCUM_DB_$Timestamp.zip"

    Write-Host "[$(Get-Date)] Iniciando backup de segurança..." -ForegroundColor Cyan

    # Backup do SCUM.db (pasta Saved)
    if (Test-Path $SavedDir) {
        Compress-Archive -Path $SavedDir -DestinationPath $BackupFileDB -CompressionLevel Optimal
    } else {
        Write-Warning "Pasta Saved não encontrada para backup do DB!"
    }

    # Backup completo opcional (pode ser pesado, removendo logs/temps se necessário)
    # Compress-Archive -Path $ServerRoot -DestinationPath $BackupFileServer -CompressionLevel Optimal

    Write-Host "[$(Get-Date)] Backup concluído." -ForegroundColor Green

    # Rotação: manter apenas as últimas 4 cópias
    $OldBackups = Get-ChildItem -Path $BackupDir -Filter "*.zip" | Sort-Object LastWriteTime -Descending | Select-Object -Skip 8 # 4 backups (cada um tem 2 arquivos se fizer full)
    foreach ($File in $OldBackups) {
        Remove-Item $File.FullName -Force
        Write-Host "Backup antigo removido: $($File.Name)" -ForegroundColor Gray
    }
}

Write-Host "--- Gerenciador de Servidor SCUM Ativo ---" -ForegroundColor Yellow

while ($true) {
    # Trava de Segurança: Se existir um arquivo STOP.txt na raiz, o script para de monitorar
    if (Test-Path (Join-Path $ServerRoot "STOP.txt")) {
        Write-Host "--- [$(Get-Date)] ARQUIVO STOP.txt DETECTADO. PARANDO MONITORAMENTO. ---" -ForegroundColor Red
        break
    }

    $Process = Get-Process -Name $ServerExe.Replace(".exe", "") -ErrorAction SilentlyContinue

    if (!$Process) {
        Write-Host "[$(Get-Date)] Servidor offline! Verificando necessidade de backup..." -ForegroundColor Red
        
        # Verificar se é hora do backup (6 da manhã)
        $CurrentHour = (Get-Date).Hour
        $CurrentMinute = (Get-Date).Minute
        
        # Se estiver entre 5:50 e 6:30, faz o backup (janela para o restart das 6:00)
        if ($CurrentHour -eq 6 -and $CurrentMinute -lt 30) {
            Create-Backup
        }

        Write-Host "[$(Get-Date)] Iniciando servidor..." -ForegroundColor Green
        # Inicia o servidor usando o bat original
        Start-Process -FilePath $StartScript -WorkingDirectory $ServerRoot
        
        # Espera 2 minutos para o servidor carregar antes de checar de novo
        Start-Sleep -Seconds 120
    }

    Start-Sleep -Seconds 30
}
