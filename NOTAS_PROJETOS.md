# Notas dos projetos (leitura obrigatória ao retomar)

## LiteLauncher — FECHADO na v59 (31/10/2026)
- Entrega final: /home/claude/v6/LiteLauncher_v59.zip (fonte em /home/claude/v6/LiteLauncher). Backups: v21, v36, v53, v54..v58.
- Stack: C#/.NET 8 WPF; Core (net8.0) + App (WPF) + testes xunit. Instalador Inno Setup via GitHub Actions ao empurrar tag. v1.0.0 já publicada; mudanças pendentes sugerem tag 1.1.0 (editar RELEASE_NOTES.md antes).
- Funções: Steam/Epic/manuais/Apps do menu Iniciar, controle/teclado, downloads Steam, mute do jogo no overlay, SteamGridDB (capas, logos, capturas), fundos 16:9 automáticos (captura da loja Steam -> Wallhaven com checagem de tags), aba "Fundos 16:9" (Wallhaven) em Gerenciar. IGDB foi removido. SteamGridDB só para capas (e picker manual).
- Nunca foi compilado aqui (sem dotnet): o usuário compila. Chaves nunca no repositório (DPAPI no settings.json).
- Roadmap NÃO iniciado (perguntar antes): provedores GOG/Xbox, remapeamento de controle, atualização automática, assinatura de código.
- Regras: responder em português; sem marcas/arte Xbox/Steam/Valve; avisar sobre RELEASE_NOTES.md antes de tag.

## Projeto novo — launcher com visual do Steam OS / Big Picture (iniciado 31/10/2026)
- Pasta: /home/claude/deck (solução própria). Reaproveita o Core do LiteLauncher v59 (namespaces DeckLauncher.Core mantidos de propósito; renomear só com compilador à mão).
- Mesmas funções: capas, fundos, logos e capturas do SteamGridDB; integração Steam e Epic; jogos manuais.
- Visual (dos 8 prints do usuário): barra superior (busca, sino, globo, relógio, avatar); barra inferior com MENU (Xbox/F1) e dicas A SELECIONAR / B VOLTAR; início com fundo grande do jogo + "Jogos recentes" (primeiro cartão largo, demais capas) + abas NOVIDADES/AMIGOS/RECOMENDADOS; menu lateral (Início, Biblioteca, Loja, Amigos e conversas, Mídias, Downloads, Configurações, Ligar/Desligar); biblioteca com abas (Todos, Instalados, Favoritos, Coleções, Não Steam...) e grade de capas; página do jogo (banner + Jogar verde + última sessão/tempo + abas); Downloads; Configurações com menu esquerdo; diálogo Ligar/Desligar.
- Sem logotipos/arte da Valve: só inspiração de layout e cores.
- Etapas: 1 esqueleto+tema+casca; 2 dados reais (Steam/Epic/manuais) no Início e Biblioteca; 3 página do jogo e Jogar; 4 Gerenciar arte (SteamGridDB); 5 Downloads/Configurações/Ligar-Desligar; 6 controle e polimento.

## Andamento do projeto novo
- Etapa 1 (v0.1): esqueleto, tema, casca. FEITA.
- Etapa 2 (v0.2): LibraryService (Steam/Epic/manuais + arte automática), Início com 'Jogos recentes' reais e fundo do jogo em foco, Biblioteca com abas (Todos/Steam/Epic/Adicionados/Favoritos), busca e grade. Pasta de dados: %AppData%\DeckLauncher (importa settings.json do LiteLauncher na 1ª execução). Clicar num jogo inicia direto (a página do jogo vem na etapa 3). Ainda sem 'não instalados' da Steam, sem Favoritos editáveis e sem controle de gamepad.
- Etapa 3 (v0.3): página do jogo (banner com logo, botão Jogar verde, última sessão, tempo de jogo, loja, favorito, abas Sobre/Informações). Enter num jogo abre a página; Esc volta ao mesmo cartão. Jogar registra a última sessão em PlayRecords. Botão engrenagem (Gerenciar arte) ainda é aviso: etapa 4. Sem controle da sessão em andamento/tempo de jogo de Epic (não há rastreio de processo ainda).
- Etapa 4 (v0.4): Gerenciar arte (engrenagem na página do jogo): abas Capas/Fundos/Logotipos (SteamGridDB, com campo para colar a chave se faltar), Fundos 16:9 (Wallhaven, sem chave), Capturas (Steam) e Restaurar padrão. A escolha vira override em GameOverrides (imagem copiada para %AppData%\DeckLauncher\games). Falta: renomear jogo, Downloads/Configurações/Ligar-Desligar (etapa 5), controle (etapa 6).
- Etapa 5 (v0.5): Downloads (progresso da Steam por manifest, estimativa entre leituras, aviso ao terminar), Configurações (Sistema: tela cheia, atualizar biblioteca, limpar cache de arte, Sobre; Integrações: chave do SteamGridDB; Jogos adicionados: adicionar .exe / remover) e Ligar/Desligar (suspender, desligar, reiniciar, finalizar sessão com confirmação; minimizar; sair). Faltam: controle/gamepad (etapa 6), renomear jogo, não instalados da Steam, instalador/GitHub Actions, ícone.
- Etapa 6 (v0.6): controle (Gamepad XInput/DirectInput portado), A/B/Start/Select/LB/RB/Y/X, dicas da barra inferior trocam entre teclado e controle, hover foca, ícones do topo funcionais (buscar, downloads, rede), ícone do app (original), README com controles. Controle só age com a janela ativa (não interfere no jogo). Projeto 'completo' em v0.6 conforme o plano; pendentes opcionais: não instalados da Steam, renomear jogo, instalador/GitHub Actions, teste real de compilação (nunca compilado aqui).

## v0.7
- Logotipos automáticos na página do jogo: cache do Steam no PC -> CDN da Steam -> SteamGridDB (chave do usuário). Salvos em %AppData%\DeckLauncher\covers (logo_*.png). Sem logotipo, mostra o nome do jogo.

## v0.8
- Menu lateral de ponta a ponta (cobre barra superior e inferior).
- Biblioteca: grade centralizada (colunas inteiras), moldura de foco sempre dentro da área visível (BringIntoView com folga), zoom 1.04.
- Ícone de rede abre painel com Wi-Fi/Ethernet conectados (IP, nome da rede via netsh) + atalho para configurações do Windows; ícone muda (Wi-Fi/Ethernet/offline).
- Novo ícone de som: painel com Silenciar, -/+ 5% e barra (Core Audio via COM em SystemVolume.cs).
- Início: tile "Veja mais na sua biblioteca" no fim da linha de recentes.

## v0.8.1
- Texto de botão focado (barra branca) ficava ilegível: o estilo global de TextBlock fixava Foreground; removido, Window define Foreground e os botões herdam (PowerItem focado = texto escuro).
- Select do controle agora abre o menu lateral (igual ao Start).

## v0.9
- Só o Select (controle) abre o menu lateral; Start não faz mais nada.
- Início: a seta para o lado para no último/primeiro jogo (sem voltar ao início) — IsListEdge em MainWindow.Motion.cs, vale para teclado e controle, Início e Biblioteca.
- Animações: tile expande com animação, fundo (hero) com crossfade, linha de recentes rola suave, páginas entram com fade/subida, página do jogo com zoom no banner, menu lateral desliza.
- Sons (sintetizados, originais) em Assets/Sounds: nav, select, back, bump, menu_open/close, launch. Opção "Efeitos sonoros" em Configurações (AppSettings.UiSounds / UiVolume).

## v0.10
- Bug: ao voltar (seta esquerda) no fim da linha de recentes o foco travava -> navegação esquerda/direita agora é por índice (TryListMove em MainWindow.Motion.cs), sem PredictFocus.
- Início mostra até 20 jogos (HomeTileCount = 20).
- Abas NOVIDADES/AMIGOS/RECOMENDADOS: seta para baixo na linha (ou clique na aba) abre FeedPage. Dados em SteamFeeds.cs (notícias ISteamNews, appmanifest LastUpdated, GetFriendList/PlayerSummaries/RecentlyPlayed com chave do usuário, ISteamChartsService, busca da loja para o calendário). Modelos em FeedModels.cs, UI em MainWindow.Feed.cs e templates no fim do Theme.xaml.
- Limitações: a atividade dos amigos é "jogou X nas últimas 2 semanas" (a Steam não expõe o feed real por API pública); o calendário mostra lançamentos populares (sem lista de desejos pessoal); tamanho em "Recém-atualizados" = tamanho instalado. Nada disso foi testado com a rede real (sem compilador/rede aqui).
- Configurações > Integrações: campo da chave da API Web da Steam (criptografada com DPAPI pelo SettingsStore).

## v0.10.1
- Novo ícone original (assets/icon.svg -> app.ico multi-tamanho em src/DeckLauncher.App/Assets/app.ico), imagem social 1280x640 (assets/social-preview.png).
- README para GitHub com logo, badges, prints (docs/screenshots/), configuração, controles e avisos; .gitignore.

## v0.10.2
- Ícone trocado: controle de videogame (gamepad) original em fundo azul-violeta. Fonte em assets/icon.svg; app.ico regenerado.

## v0.11.0
- GitHub Actions: .github/workflows/build.yml (build+test) e release.yml (tag v* -> dois zips + SHA256SUMS + release).
- Iniciar com o Windows: Autostart.cs (HKCU Run), switch em Configurações > Sistema.
- Tempo de jogo Epic/manual: MainWindow.Play.cs (a cada 15 s lista processos via Native.RunningImagePaths, soma minutos em PlayRecords e grava PlaySessions).
- Renomear/esconder: botão "..." na página do jogo (MainWindow.Options.cs); AppSettings.HiddenGames; lista "Jogos escondidos" em Configurações > Jogos; LibraryService filtra os escondidos.

## v0.18.0 — Amigos com chat, roda de mensagens rápidas, Configurações > Interface
- Menu lateral "Amigos" (antes "Amigos e conversas" abria placeholder): MainWindow.Friends.cs. Lista on-line/off-line via SteamFeeds.FetchFriendsAsync (agora lê personastate; FriendData.PersonaState), atualiza a cada 30 s. Conversa à direita; B volta do chat para a lista.
- Chat: SteamChat.cs fala só com api.steampowered.com (IFriendMessagesService GetRecentMessages/SendMessage; polling de 4 s com a conversa aberta). Login por QR aprovado no app da Steam (IAuthenticationService BeginAuthSessionViaQR/PollAuthSessionStatus, platform MobileApp) -> refresh token em AppSettings.SteamChatToken (campo secreto, DPAPI); access token por GenerateAccessTokenForApp. NÃO TESTADO COM A STEAM REAL (sem rede/compilador aqui): se o login falhar, mostrar o texto do erro; "ABRIR NO STEAM" (steam://friends/message/ID) é o plano B. Envia via input_json (POST form).
- QR desenhado com o pacote NuGet QRCoder 1.6.0 (PngByteQRCode).
- Roda: Gamepad.cs ganhou SelectHoldStarted/Ended/Tapped (segurar 350 ms, sem Start/B) e RightStick (XInput; PlayStation/Switch pelo winmm Z/R). Numa conversa o Select só abre o menu ao soltar rápido (OnSelectTapped). A envia, B/soltar Select fecha; setas como alternativa.
- Configurações > Interface: Preto OLED (movido de Sistema) + edição das 8 mensagens (AppSettings.QuickMessages, NormalizeQuickMessages). Integrações: linha "Chat da Steam" com SAIR DO CHAT.
- Testes novos: QuickMessageTests.cs.

## v0.18.1
- Corrigido CS0102: campo Portuguese duplicado (já existe em MainWindow.GamePage.cs); o de Friends agora se chama PtBr.

## v0.18.2
- Corrigido CS0103: faltava using System.Net.Http em MainWindow.Friends.cs (projetos WPF não o importam implicitamente).

## v0.18.3 — chat reescrito com SteamKit2
- Erro 404 ao enviar: IFriendMessagesService/SendMessage NÃO existe na API web (só GetRecentMessages etc.). Envio/recebimento agora via SteamKit2 3.4.0 (SteamLink.cs): QR (BeginAuthSessionViaQRAsync) -> AccountName + RefreshToken salvos como "conta:token" em AppSettings.SteamChatToken (formato antigo sem conta é descartado); LogOn com AccessToken=refresh token, LoginID fixo; SetPersonaState(Online) enquanto a página Amigos está aberta (a sessão cai ao sair da página).
- Mensagens recebidas chegam por FriendMsgCallback (sem polling). Histórico: GetRecentMessages via web com access token, melhor esforço (erros ignorados). Mensagens enviadas por outro dispositivo não aparecem (sem FriendMsgEcho).
- O login por QR do v0.18.0 funcionou (REST), mas foi substituído porque o refresh token agora precisa da conta para o LogOn.

## v0.18.4
- "Últimas duas semanas" sob o card não atualizava: só jogos fora da Steam eram monitorados e a sessão só era gravada ao fechar o jogo. Agora MainWindow.Play.cs monitora todos os jogos, grava a sessão minuto a minuto (_liveSessions), só soma PlayRecords.Minutes fora da Steam (a Steam tem total próprio) e atualiza o texto (GameItem.RefreshRecent / IsRunning -> "JOGANDO AGORA") e a legenda do card em foco.

## v1.0.0
- Versão final: README reescrito (todas as funções, 7 prints novos em docs/screenshots, Download simplificado, removidos Como rodar/Gerar executável/Testes/Publicar). Tag sugerida: v1.0.0.

## v1.0.0 — instalador
- Release agora gera só DeckLauncher-Setup-<versão>.exe (Inno Setup, installer/DeckLauncher.iss, igual ao LiteLauncher); sem zips. Instalação por usuário por padrão (PrivilegesRequired=lowest, pode pedir admin), pt-BR, atalho opcional na área de trabalho, abre o programa ao final. Dados do usuário (%AppData%\DeckLauncher) não são apagados ao desinstalar.

## v1.0.1 — chat removido
- A Steam restringiu a conta do usuário depois do login por QR (sessão SteamKit2 como cliente). TUDO do chat foi removido: SteamKit2/QRCoder, SteamLink/SteamChat, menu "Amigos e conversas", roda de mensagens rápidas, SteamChatToken/QuickMessages, Select-hold e analógico direito no Gamepad. Fica só a aba AMIGOS sob os cards (API pública com chave). NUNCA reintroduzir login/sessão de cliente na Steam.

## v1.1.0 — aba Aplicativos
- Biblioteca > APLICATIVOS (MainWindow.Apps.cs): lê o menu Iniciar uma vez (StartApps.ReadAsync, cache de 3 min), mostra Discord, Explorador de Arquivos, Edge, Microsoft Store, Configurações, AMD Adrenalin e NVIDIA App só se instalados (Explorador/Store/Configurações têm fallback por exe/URI). Ícone via ShellThumbnail (shell:AppsFolder\<AppID>), estilo MediaTile das Lojas, nome embaixo. Tile "+" adiciona .exe/.lnk (AppSettings.ManualApps, ManualApp.cs); X remove. Navegação por controle em TryListMove (444 px por coluna). AppsShown ficou sem uso.

## v1.2.0 — atualizações e Aplicativos completos
- Atualizações: MainWindow.Update.cs usa UpdateChecker (GitHub releases/latest, sem chave). Constante UpdateRepository = "SEU-USUARIO/DeckLauncher" PRECISA ser trocada pelo "dono/repo" real (enquanto for o placeholder, a verificação fica desligada). Aviso (toast) uma vez por versão (AppSettings.NotifiedUpdate); linha em Configurações > Sistema com VERIFICAR/BAIXAR. Nada é baixado/instalado sozinho.
- Aplicativos: sites (ManualApp.Url, favicon via google.com/s2/favicons, só o domínio é enviado), ordem (AppsOrder, chaves "b:Nome"/"m:caminho-ou-url"), ocultar (HiddenApps) e tile "Mostrar ocultos". X/botão direito abre menu (AppMenuPanel); "+" abre AppAddPanel (programa ou site). Helpers testáveis em Core/Apps/AppList.cs (+ AppListTests.cs).

## v1.2.1
- Fluxo do sofá (botão Guia, v1.3.0 de teste) REVERTIDO: o Windows abria o Game Bar junto. Base volta a ser a v1.2.0.
- Amigos: seção "Amigos online" na aba AMIGOS (FriendChatCard) que abre steam://friends/message/<id> no cliente oficial da Steam; link validado por SteamFriends.ChatUrl (só números). Nenhum login/sessão da Steam no launcher.
- Limpeza: namespaces e pastas renomeados LiteLauncher.Core -> DeckLauncher.Core (e .Tests); mantidos de propósito por compatibilidade: pasta %AppData%\LiteLauncher (importação), Entropy "LiteLauncher.settings.v1" (DPAPI) e Format "LiteLauncher-settings" (backup). AppsShown removido (JSON antigo com essa chave é ignorado). Testes novos: FriendChatAndBackupTests.cs.

## v1.4.0 — Modo Xbox (FSE) por padrão (NÃO TESTADO NO WINDOWS: sem compilador no sandbox)
- Problema: no Modo Xbox (AnyFSE) Select+B segurado não abria o menu do jogo. Causa provável: sem a capacidade "gamingHome" o Windows não deixa o launcher assumir a frente sobre o jogo. Não foi possível reproduzir aqui; por isso há registro de diagnóstico.
- Modelo: Playnite-FSE-Launcher (leeson865). Pacote MSIX "DeckLauncherFSE" (fse/AppxManifest.xml com windows.gamingApp + CustomCapability Microsoft.appCategory.gamingHome_8wekyb3d8bbwe + fse/CustomCapability.SCCD) contendo a ponte src/DeckLauncher.Fse (net48, DeckLauncherFSE.exe): mutex por sessão, janela-handshake 1,5 s, abre DeckLauncher.exe --xbox (caminho em HKCU\Software\DeckLauncher\InstallDir, gravado pelo instalador) e fica vivo até ele fechar.
- Build: fse/Build-Fse.ps1 (release.yml, runner windows) compila a ponte, monta, empacota com makeappx, gera certificado CN=DeckLauncher e assina com signtool; instalador (installer/DeckLauncher.iss) tem a tarefa "xboxmode" (marcada por padrão) que roda installer/fse/Install-FSE.ps1 como administrador (confia no .cer em LocalMachine\TrustedPeople, Add-AppxPackage; se falhar liga AllowDevelopmentWithoutDevLicense temporariamente e restaura). Desinstalar remove o pacote (Remove-FSE.ps1). Log: %TEMP%\DeckLauncher-ModoXbox.log.
- App: XboxMode.cs (--xbox => tela cheia + xbox-mode.log); OpenInGameMenu agora registra o combo e, se o launcher não ficar ativo em 600 ms, minimiza a janela do jogo (Native.Minimize) e tenta de novo; Gamepad.Activity registra se o controle chega com o launcher em segundo plano.
- Requer testar: (1) se o pacote instala e aparece em Configurações > Jogos > Experiência de tela cheia; (2) Select+B no Modo Xbox; (3) ler xbox-mode.log. O Guia é exclusivo do Windows no Modo Xbox (toque abre Game Bar, longo = alternador de tarefas) — por isso o botão Guia não serve (v1.3.0 revertida).
- Backups: backup_v1.2.0 e DeckLauncher_v1.2.1.zip continuam intactos.

## v1.5.0 — Steam: jogos não instalados e Instalar
- Aba "NÃO INSTALADOS" na Biblioteca (lista separada _notInstalled; NÃO entra em _all, então Início/Todos/Favoritos não mudam). LibraryService.LoadNotInstalledAsync usa SteamOwnedGames (API com chave, senão XML público do perfil), cache de 15 min, capas via CoverDownloader (máx. 40 por execução). Mensagens vazias: noid / fetch / tudo instalado.
- Página do jogo: botão vira "Instalar" (steam://install/<id> pelo cliente da Steam; progresso na página Downloads já existente) e há "VER NA LOJA" (steam://store/<id>) para jogos Steam. SteamDownloads.StoreUri + teste. Nada de login/sessão da Steam.
- Base anterior: v1.4.0 (Modo Xbox, ainda sem retorno de teste).

## v1.5.1 — correção da release (instalador sem out\fse)
- Erro no GitHub: "No files found matching ...\out\fse\*" (o passo do pacote do Modo Xbox não gerou arquivos; também é possível que a pasta oculta .github não tenha sido atualizada no envio). O instalador agora só inclui o Modo Xbox se out\fse\DeckLauncherFSE.msix existir (diretiva #ifdef HasFse); o passo do pacote roda em Windows PowerShell 5.1 (certificados) com continue-on-error e emite aviso se falhar.
- Lembrete: a tag precisa ser a da versão (v1.5.1); a tag v1.0.1 já foi usada antes.
