# --- CONFIGURATION ---
$serverRoot = $PSScriptRoot
if (-not $serverRoot) { $serverRoot = Get-Location }

$licenseFileName = "oxygen_license.json"
$apiUrl = "https://oxymod.com/api/server/plugin/check-access"
$mainDllPath = Join-Path $serverRoot "SCUM\Binaries\Win64\oxygen.core.dll"
$licenseFilePath = Join-Path $serverRoot $licenseFileName
$webPort = 8447

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "      OXYGEN SERVER PLUGIN UPDATER      " -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# --- DATA MANAGEMENT ---
function Get-LicenseData {
    if (Test-Path $licenseFilePath) {
        try { 
            $content = Get-Content $licenseFilePath -Raw
            return $content | ConvertFrom-Json 
        } catch { return $null }
    }
    return $null
}

function Save-LicenseData($token, $hash) {
    $data = @{ 
        api_token = $token; 
        last_hash = $hash 
    }
    $data | ConvertTo-Json | Out-File $licenseFilePath -Encoding utf8
}

# --- CHECK FUNCTION ---
function Check-Requirements {
    Write-Host "[...] Checking system requirements..." -ForegroundColor Gray
    $missing = @()
    $links = @()

    # 1. VC++ Redist check
    if (-not (Test-Path "HKLM:\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64")) {
        $missing += "Microsoft Visual C++ 2015-2022 Redistributable (x64)"
        $links += "VC++ (Direct): https://aka.ms/vs/17/release/vc_redist.x64.exe"
    }

    # 2. .NET Runtime check
    $dotnetVer = dotnet --list-runtimes 2>$null | Select-String "Microsoft.NETCore.App 8."
    if (-not $dotnetVer) {
        $missing += ".NET Runtime 8.0 (x64)"
        # Пряме посилання на завантаження консольного рантайму
        $links += ".NET 8.0: https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-8.0.24-windows-x64-installer"
    }
    
    # 3. OpenSSL Check (Strict file check)
    $sslFound = $false
    $dllName = "libssl-3-x64.dll"
    $sslPaths = @(
        "$env:SystemRoot\System32\$dllName",
        "$env:SystemRoot\SysWOW64\$dllName",
        "C:\Program Files\OpenSSL-Win64\bin\$dllName"
    )

    foreach ($path in $sslPaths) {
        if (Test-Path $path) { $sslFound = $true; break }
    }

    if (-not $sslFound) {
        $missing += "OpenSSL 3.x (x64) - libssl-3-x64.dll not found"
        $links += "OpenSSL: https://slproweb.com/products/Win32OpenSSL.html (Win64 OpenSSL v3.5.x EXE)"
    }

    # ВИВІД ПОМИЛОК
    if ($missing.Count -gt 0) {
        Write-Host "`n[!] MISSING COMPONENTS DETECTED:" -ForegroundColor Red
        foreach ($item in $missing) { 
            Write-Host "  - $item" -ForegroundColor Red 
        }

        Write-Host "`n[!] DOWNLOAD LINKS:" -ForegroundColor Yellow
        foreach ($link in $links) {
            Write-Host "  $link" -ForegroundColor Cyan
        }

        Write-Host "`nPlease install all components and restart the updater." -ForegroundColor Yellow
        Write-Host "----------------------------------------"
        Pause; exit
    }
    
    Write-Host "[OK] All system dependencies found." -ForegroundColor Green

    # 4. FIREWALL CHECK
    Write-Host "[...] Checking Windows Firewall for port $webPort..." -ForegroundColor Gray
    $fwRule = Get-NetFirewallRule -DisplayName "Oxygen Web Link" -ErrorAction SilentlyContinue
    if (-not $fwRule) {
        Write-Host "[!] Firewall rule for port $webPort is missing." -ForegroundColor Yellow
        $choice = Read-Host "Do you want to create the 'Oxygen Web Link' firewall rule now? (Y/N)"
        if ($choice -match "Y") {
            try {
                New-NetFirewallRule -DisplayName "Oxygen Web Link" -Direction Inbound -LocalPort $webPort -Protocol TCP -Action Allow -ErrorAction Stop | Out-Null
                Write-Host "[OK] Firewall rule created successfully." -ForegroundColor Green
            } catch {
                Write-Host "[ERROR] Failed to create rule. Please run as Administrator." -ForegroundColor Red
                Pause; exit
            }
        }
    } else { Write-Host "[OK] Firewall rule is already present." -ForegroundColor Green }
}

# --- MAIN PROCESS ---
Check-Requirements

$config = Get-LicenseData

if ($null -eq $config -or [string]::IsNullOrWhiteSpace($config.api_token)) {
    Write-Host "[!] FIRST RUN: Activation Required" -ForegroundColor Yellow
    $tokenInput = Read-Host "Please enter your api_token"
    if ([string]::IsNullOrWhiteSpace($tokenInput)) { exit }
    Save-LicenseData $tokenInput.Trim() ""
    $config = Get-LicenseData
}

$body = @{ api_token = $config.api_token } | ConvertTo-Json

try {
    Write-Host "[...] Checking for updates..." -ForegroundColor Gray
    $response = Invoke-WebRequest -Uri $apiUrl -Method Post -Body $body -ContentType "application/json" -UseBasicParsing -TimeoutSec 20
    $data = $response.Content | ConvertFrom-Json

    if ($data.status -eq "success" -and $data.has_access -eq $true) {
        
        $remoteVer = [version]($data.version.Trim())
        $remoteHash = $data.hash.Trim().ToUpper()

        if (Test-Path $mainDllPath) {
            $rawLocal = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($mainDllPath).FileVersion
            $localVer = [version]($rawLocal.Replace(",", ".").Trim())
        } else {
            $localVer = [version]"0.0.0"
        }

        $displayHash = "NONE"
        if (![string]::IsNullOrEmpty($config.last_hash)) {
            $displayHash = $config.last_hash.Substring(0,8)
        }
        $localHash = $config.last_hash

        Write-Host "Version: $localVer | Last Sync Hash: $displayHash..." -ForegroundColor Gray

        if (($remoteVer -gt $localVer) -or ($localHash -ne $remoteHash)) {
            Write-Host "[!] Update found! Synchronizing files..." -ForegroundColor Green
            
            # Close server if run
            $runningProcesses = Get-Process | Where-Object { $_.Path -like "$serverRoot*" } -ErrorAction SilentlyContinue
            if ($runningProcesses) {
                Write-Host "[!] Stopping server processes..." -ForegroundColor Yellow
                $runningProcesses | Stop-Process -Force -ErrorAction SilentlyContinue
                Start-Sleep -Seconds 5
            }

            Get-ChildItem -Path $serverRoot -Filter *.dll -Recurse | ForEach-Object { $_.IsReadOnly = $false }

            $tempZip = Join-Path $serverRoot "Oxygen_Update.zip"
            Write-Host "[...] Downloading..." -ForegroundColor Gray
            Invoke-WebRequest -Uri $data.download_url -OutFile $tempZip

            Write-Host "[...] Installing files..." -ForegroundColor Gray
            Expand-Archive -Path $tempZip -DestinationPath $serverRoot -Force
            Remove-Item $tempZip -Force

            Save-LicenseData $config.api_token $remoteHash
            
            Write-Host "[SUCCESS] Oxygen updated successfully!" -ForegroundColor Green
        } else {
            Write-Host "[OK] Oxygen is up to date." -ForegroundColor Gray
        }
    }
} catch {
    Write-Host "[ERROR] Failed to reach API: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Update completed. Press any key to exit..."
$null = [System.Console]::ReadKey($true)