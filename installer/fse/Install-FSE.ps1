# Instala o Modo Xbox do DeckLauncher (pacote DeckLauncherFSE). O instalador chama este script pedindo permissão de administrador.
# Faz: confiar no certificado do pacote -> instalar o pacote (ligando o Modo de desenvolvedor só se for preciso e desligando depois).
$ErrorActionPreference = "Stop"
$log = Join-Path $env:TEMP "DeckLauncher-ModoXbox.log"
function Say($text) { "$(Get-Date -Format s)  $text" | Add-Content -Path $log -Encoding UTF8 }
Say "--- instalando o Modo Xbox ---"

try {
    $dir = $PSScriptRoot
    $msix = Join-Path $dir "DeckLauncherFSE.msix"
    $cer = Join-Path $dir "DeckLauncher.cer"
    if (-not (Test-Path $msix) -or -not (Test-Path $cer)) { throw "Arquivos do pacote não encontrados em $dir" }

    Import-Certificate -FilePath $cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
    Say "certificado confiável"

    $key = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock"
    $name = "AllowDevelopmentWithoutDevLicense"
    $changed = $false
    $before = $null
    try {
        Add-AppxPackage -Path $msix -ForceUpdateFromAnyVersion
        Say "pacote instalado"
    }
    catch {
        Say "primeira tentativa falhou: $($_.Exception.Message)"
        # Capacidades do Modo Xbox só entram com o Modo de desenvolvedor ligado (o mesmo interruptor das Configurações do Windows).
        if (-not (Test-Path $key)) { New-Item -Path $key -Force | Out-Null }
        $before = (Get-ItemProperty -Path $key -Name $name -ErrorAction SilentlyContinue).$name
        Set-ItemProperty -Path $key -Name $name -Value 1 -Type DWord
        $changed = $true
        Say "Modo de desenvolvedor ligado temporariamente"
        try {
            Add-AppxPackage -Path $msix -ForceUpdateFromAnyVersion
            Say "pacote instalado (segunda tentativa)"
        }
        finally {
            if ($null -eq $before) { Remove-ItemProperty -Path $key -Name $name -ErrorAction SilentlyContinue }
            else { Set-ItemProperty -Path $key -Name $name -Value $before -Type DWord }
            Say "Modo de desenvolvedor devolvido ao que era"
        }
    }
    exit 0
}
catch {
    Say "ERRO: $($_.Exception.Message)"
    exit 1
}
