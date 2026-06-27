# Install.ps1
# Automates certificate trusting and app installation for JournalApp

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$cerFile = Get-ChildItem -Path $ScriptDir -Filter *.cer | Select-Object -First 1
$msixFile = Get-ChildItem -Path $ScriptDir -Filter *.msix | Select-Object -First 1

if ($cerFile -and $msixFile) {
    Write-Host "==========================================" -ForegroundColor Cyan
    Write-Host "     JournalApp Automated Installer" -ForegroundColor Cyan
    Write-Host "==========================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Found Certificate: $($cerFile.Name)" -ForegroundColor Yellow
    Write-Host "Found Installer  : $($msixFile.Name)" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Trusting certificate (CurrentUser\TrustedPeople)..." -ForegroundColor White
    
    try {
        $store = New-Object System.Security.Cryptography.X509Certificates.X509Store("TrustedPeople", "CurrentUser")
        $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
        $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($cerFile.FullName)
        
        # Avoid duplicate imports
        $alreadyExists = $false
        foreach ($existingCert in $store.Certificates) {
            if ($existingCert.Thumbprint -eq $cert.Thumbprint) {
                $alreadyExists = $true
                break
            }
        }
        
        if (-not $alreadyExists) {
            $store.Add($cert)
            Write-Host "✓ Certificate trusted successfully." -ForegroundColor Green
        } else {
            Write-Host "✓ Certificate was already trusted." -ForegroundColor Green
        }
        $store.Close()
    }
    catch {
        Write-Error "Failed to install certificate: $_"
    }

    Write-Host ""
    Write-Host "Launching App Installer..." -ForegroundColor Cyan
    Start-Process -FilePath $msixFile.FullName
} else {
    Write-Error "Required .cer or .msix files were not found in the installer directory."
}
