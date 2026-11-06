<#
  Gera o pacote do Modo Xbox: out\fse\DeckLauncherFSE.msix (assinado com um certificado próprio) + DeckLauncher.cer + scripts.
  Uso (no Windows, com o SDK do Windows instalado; o runner windows-latest do GitHub já tem):
      pwsh fse\Build-Fse.ps1 -Version 1.2.1
#>
param(
    [Parameter(Mandatory)] [string] $Version,
    [string] $OutDir = "out\fse"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# 1.2.1 ou 1.2.1-dev -> 1.2.1.0 (o pacote exige 4 números)
$numbers = ($Version -split '[-+]')[0].Split('.') | ForEach-Object { [int]$_ }
while ($numbers.Count -lt 4) { $numbers += 0 }
$packageVersion = ($numbers[0..3] -join '.')

$stage = Join-Path $root "out\fse-stage"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage, $OutDir | Out-Null

# 1) a ponte (.NET Framework 4.8)
dotnet build src\DeckLauncher.Fse\DeckLauncher.Fse.csproj -c Release -o "$stage\bridge"
if ($LASTEXITCODE -ne 0) { throw "Falha ao compilar a ponte do Modo Xbox" }
Copy-Item "$stage\bridge\DeckLauncherFSE.exe" $stage
Remove-Item "$stage\bridge" -Recurse -Force

# 2) manifesto, capacidade (.SCCD) e imagens
(Get-Content fse\AppxManifest.xml -Raw -Encoding UTF8).Replace("{{VERSION}}", $packageVersion) |
    Set-Content (Join-Path $stage "AppxManifest.xml") -Encoding UTF8
Copy-Item fse\CustomCapability.SCCD $stage
Copy-Item fse\Images (Join-Path $stage "Images") -Recurse
Copy-Item fse\Public (Join-Path $stage "Public") -Recurse

# 3) ferramentas do SDK do Windows
$kits = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
$makeappx = Get-ChildItem $kits -Recurse -Filter makeappx.exe | Where-Object FullName -like "*\x64\*" | Sort-Object FullName -Descending | Select-Object -First 1
$signtool = Get-ChildItem $kits -Recurse -Filter signtool.exe | Where-Object FullName -like "*\x64\*" | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $makeappx -or -not $signtool) { throw "makeappx/signtool não encontrados (instale o SDK do Windows)" }

# 4) empacotar
$msix = Join-Path $OutDir "DeckLauncherFSE.msix"
if (Test-Path $msix) { Remove-Item $msix -Force }
& $makeappx.FullName pack /d $stage /p $msix /o
if ($LASTEXITCODE -ne 0) { throw "makeappx falhou" }

# 5) certificado próprio (o "Publisher" do manifesto é CN=DeckLauncher) e assinatura
$password = ConvertTo-SecureString -String ([guid]::NewGuid().ToString()) -Force -AsPlainText
$cert = New-SelfSignedCertificate -Type Custom -Subject "CN=DeckLauncher" -KeyUsage DigitalSignature -FriendlyName "DeckLauncher" `
    -CertStoreLocation "Cert:\CurrentUser\My" -NotAfter (Get-Date).AddYears(10) `
    -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
$pfx = Join-Path $stage "sign.pfx"
Export-PfxCertificate -Cert $cert -FilePath $pfx -Password $password | Out-Null
Export-Certificate -Cert $cert -FilePath (Join-Path $OutDir "DeckLauncher.cer") | Out-Null
$plain = [System.Net.NetworkCredential]::new("", $password).Password
& $signtool.FullName sign /fd SHA256 /f $pfx /p $plain $msix
if ($LASTEXITCODE -ne 0) { throw "signtool falhou" }
Remove-Item $pfx -Force
Remove-Item "Cert:\CurrentUser\My\$($cert.Thumbprint)" -Force

Copy-Item installer\fse\*.ps1 $OutDir
Get-ChildItem $OutDir
