# Remove o pacote do Modo Xbox (rodado pelo desinstalador; não precisa de administrador).
Get-AppxPackage -Name "DeckLauncherFSE" -ErrorAction SilentlyContinue | Remove-AppxPackage -ErrorAction SilentlyContinue
