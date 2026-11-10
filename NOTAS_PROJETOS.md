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

## v1.6.0 — estilos da tela inicial: Painel e Sala (OLED)
- Configurações > Interface > "Estilo da tela inicial": Clássico (padrão, o de sempre), Painel e Sala (OLED). Salvo em AppSettings.HomeStyle ("Xbox" | "Panel" | "Room"; valores antigos "Simple"/"Cinema" voltam para "Xbox"). Troca na hora, sem reiniciar.
- Painel: jogo em destaque (último jogado, com logo e "Jogar") + grade 3x2 dos recentes + atalhos Biblioteca (nº de jogos), Downloads (em andamento/%), Aplicativos, Amigos (online/jogando, precisa da chave da API Web) e Capturas. Fundo com a arte do jogo em foco.
- Sala: fundo preto puro, relógio grande, saudação + amigos online, 6 ícones arredondados dos recentes (o focado cresce), nome e tempo recente do jogo e chips Biblioteca/Lojas/Aplicativos/Downloads/Amigos.
- Código: MainWindow.HomeStyles.cs (ApplyHomeStyle, FillHomeStyles, FocusHome por estilo, cliques/atalhos, relógio e info); estilos novos no Theme.xaml (PanelTile, RoomIcon, InfoTile, RoomChip, StyleCard); HomePage agora tem ClassicHome/PanelHome/RoomHome. Navegação por controle usa o foco direcional padrão nos dois novos estilos.
- NÃO TESTADO NO WINDOWS (sem compilador no sandbox).
- PONTO DE REVERSÃO: backup_v1.5.1 (+ DeckLauncher_v1.5.1.zip) = versão sem os novos estilos. Os dados salvos são compatíveis (a v1.5.1 ignora/normaliza HomeStyle).
- Não escolhidos por enquanto: Cinema, Carrossel, cores de destaque, tamanho dos cards, "fundo do jogo em foco" e "relógio grande" como opções.

## v1.6.1 — ajustes do Painel/Sala
- Textos dos atalhos do Painel agora brancos (InfoTile com Foreground).
- Biblioteca sempre abre na aba "Todos" ao vir de outra tela (exceto ao voltar de um jogo); o atalho Aplicativos continua abrindo a aba de aplicativos (ShowPage em MainWindow.xaml.cs).
- Navegação explícita (1 aperto = 1 passo) no Painel e na Sala: TryHomeStyleMove em MainWindow.HomeStyles.cs, chamada no início de TryListMove.
- Reversão: backup_v1.5.1.

## v1.6.2
- Corrigido erro de build CS0246 (faltava using System.Windows.Input em MainWindow.HomeStyles.cs).

## v1.6.3 — Biblioteca: subir sem atraso + seletor suave
- Seta para cima na grade da Biblioteca/Aplicativos agora é explícita (uma linha por vez, como o Down) em TryListMove; antes usava a busca geométrica padrão do WPF, que atrasava ao subir.
- Seletor branco deslizante (LibCursor, Canvas sobre LibGrid em MainWindow.xaml) anima até a capa focada (170 ms) e a lista rola suavemente (LibOffsetProperty, 220 ms) em OnLibTileFocus; a capa cresce com animação (LibTile no Theme.xaml). Sem teste no Windows.

## v1.7.0 — opções extras de Interface (conceito 5)
- Configurações > Interface: Cor de destaque (Azul padrão, Verde, Roxo, Laranja, Rosa, Vermelho = AppSettings.AccentName; muda o pincel "Accent" em tempo de execução), Tamanho dos cards da biblioteca (Pequeno 0,85 / Médio 1 / Grande 1,15 = CardSize; LayoutTransform em LibGrid, só Biblioteca) e Relógio e saudação grandes (BigClock: relógio da barra superior e da Sala).
- Código: MainWindow.Appearance.cs; estilos Swatch/SegButton no Theme.xaml; testes em AppearanceSettingsTests.cs. "Fundo do jogo em foco" NÃO foi feito (não pedido).
- Reversão: backup_v1.5.1 (sem Painel/Sala/extras). Sem teste no Windows.

## v1.7.1
- Cor de destaque: o pincel "Accent" agora é DynamicResource em todo o XAML e é trocado no Application.Resources (antes alterar a cor não surtia efeito). Afeta chaves ligadas, barras de progresso, marcadores, títulos de painéis e o seletor Pequeno/Médio/Grande.

## v1.7.2 — repositório, Sala com mais informação, limpeza
- UpdateRepository = "dupreys/DeckLauncher" (verificação de atualização ligada; README com os links do repositório). Se o repositório tiver outro nome, ajustar em MainWindow.Update.cs e no README.
- Sala (OLED): linha de status no canto (bateria com "carregando" quando houver, e Wi-Fi/Cabo/Sem rede; atualiza a cada 30 s) e chip "▶ Jogando: <jogo>" quando um jogo está aberto (abre a página dele). Native.Battery() (GetSystemPowerStatus).
- Limpeza em MainWindow.HomeStyles.cs: helpers StepInRow/Directions/Go/SetChoice/OnChoiceClick (os cliques de Interface — estilo, cor, tamanho — usam o mesmo caminho), FocusHome único, UpdateRoomClock virou UpdateRoomStatus, DownloadsSummary separado.
- Instalador: criar a tag v1.7.2 no GitHub (release.yml). Backup de reversão continua sendo backup_v1.5.1.

## v1.8.0 — contas Steam, dicas contextuais e avisos
- Contas Steam (MainWindow.Accounts.cs, SteamAccount.cs): lê TODAS as contas de config\loginusers.vdf (SteamLoginUsers.ReadAll). Com 2+ contas, ao abrir o launcher pergunta qual usar ("QUEM ESTÁ JOGANDO?", cartões com a foto de config\avatarcache\<id>.png). Configurações > Integrações > "Conta Steam ao abrir": Perguntar sempre (padrão) / Última conta usada / Sempre a mesma (botão CONTA: nome alterna entre as contas). Foto do perfil na barra superior > "Trocar de conta Steam...".
- Filtro: só jogos Steam instalados da conta — manifest LastOwner (Game.Owner) OU appid em userdata\<id32>\config\localconfig.vdf (SteamAccountFilter.Keep). Jogos Epic/manuais sempre aparecem. Com uma só conta nada é filtrado. A conta ativa também define amigos/perfil/jogos não instalados (SteamIdValue).
- LIMITE: o launcher NÃO faz login (regra: nunca logar/sessão de cliente Steam). Trocar de conta aqui só muda o filtro/perfil; para jogar com outra conta, o cliente Steam precisa estar logado nela. Nome e foto escolhidos em "Perfil" (CustomName/CustomAvatarPath) continuam únicos para todas as contas.
- Dicas contextuais (MainWindow.Hints.cs): o rodapé muda a palavra do A (ABRIR, JOGAR, VOLTAR AO JOGO, ALTERNAR, ESCOLHER) e, com controle, mostra X (FAVORITAR/OPÇÕES), Y (BUSCAR) e LB RB (ABAS).
- Avisos (MainWindow.Alerts.cs): bateria baixa (20/10/5 %, só sem carregar) e "<jogo> terminou de baixar" mesmo fora da página Downloads (checagem a cada 20 s). Aparecem como toast no launcher; não aparecem por cima de um jogo.
- Testes: SteamAccountTests.cs. Sem teste no Windows (sem compilador no sandbox). Reversão: backup_v1.5.1.

## v1.8.1 — troca a conta do cliente Steam junto
- SteamClientSwitch.cs: ao escolher a conta no launcher, define HKCU\Software\Valve\Steam\AutoLoginUser (+RememberPassword=1) e, se a Steam estiver aberta, fecha com "steam.exe -shutdown" (espera até 20 s) e abre de novo com "-silent". É o mesmo mecanismo do "lembrar senha" do próprio cliente: o launcher NÃO recebe, digita nem guarda senha e NÃO cria sessão/login (a regra de nunca logar na Steam continua valendo). Só faz isso se a conta tem RememberPassword=1 em loginusers.vdf e se não há jogo aberto; senão avisa por toast.
- Opção: AppSettings.SwitchSteamClient (padrão ligado), em Configurações > Integrações > "Trocar também a conta da Steam". Pode desligar para voltar a só filtrar.

## v1.8.1 — trocar também a conta do cliente Steam
- Ao trocar/escolher a conta no launcher, a Steam é reiniciada em segundo plano na conta escolhida (MainWindow.SteamClient.cs): `steam.exe -shutdown` → grava HKCU\Software\Valve\Steam AutoLoginUser/RememberPassword e marca MostRecent no loginusers.vdf (backup .launcher-backup; SteamAccountSwitch.MarkMostRecent, com teste) → `steam.exe -silent`. Só reinicia se a Steam estiver em outra conta; não faz nada com um jogo aberto.
- NÃO é login: o launcher não lê/guarda senha nem chama a API de login da Steam (continua valendo a regra de nunca reintroduzir login/sessão de cliente). Depende do login que a Steam já guardou ("Lembrar senha"); sem isso a Steam mostra a própria tela de login.
- Opção em Configurações > Integrações ("Trocar também a conta do cliente Steam", ligada por padrão; AppSettings.SwitchSteamClient). Sem teste no Windows.

## v1.9.0 — estilos Cinema e Carrossel
- Interface > Estilo da tela inicial agora tem 5 cartões: Clássico, Painel, Sala (OLED), Cinema e Carrossel (HomeStyle: "Xbox"|"Panel"|"Room"|"Cinema"|"Carousel").
- Cinema (CinemaHome): arte do jogo ao fundo com degradê, logo/título grande do jogo em foco, "JOGANDO AGORA"/"JOGOS RECENTES", tempo recente, botões Jogar (abre a página do jogo)/Biblioteca/Aplicativos/Amigos e fileira de 7 capas embaixo (foco troca título e fundo).
- Carrossel (CarouselHome): relógio e data no canto, pôsteres em fila (até 9) com o focado maior no centro (a fileira desliza, CenterCarouselOn), nome, tempo recente, pontos indicadores e atalhos Biblioteca/Aplicativos/Downloads/Amigos.
- Navegação explícita para ambos via TryTwoRows (também usada na Sala). Código novo: MainWindow.HomeCinema.cs; estilos CinemaTile/CarouselTile no Theme.xaml. "Relógio grande" também aumenta o relógio do Carrossel. Sem teste no Windows. Reversão: backup_v1.5.1.

## v1.9.1
- Corrigido erro de build CS0102 (SwitchSteamClient duplicado em AppSettings). Havia duas implementações da troca da conta do cliente Steam no projeto; ficou só SteamClientSwitch.cs (+SyncSteamClientAsync e a opção "Trocar também a conta da Steam"), que exige "lembrar senha" na conta.

## v1.9.2
- Carrossel: o pôster em foco aparecia cortado em cima (o aumento era só visual, ScaleTransform, e passava do limite da fileira). Agora a largura/altura do pôster são animadas de verdade (250x340 -> 325x442) e o centro é calculado pelos tamanhos (CenterCarouselOn(index)).

## v1.9.3
- Carrossel: a fileira era cortada na largura da tela (os últimos jogos não apareciam); agora fica dentro de um Canvas, que não limita a largura.
- Cinema e Carrossel mostram os mesmos jogos do Clássico (HomeTileCount = 20, antes 7 e 9). Cinema: fileira com rolagem horizontal automática ao mover o foco.

## v1.9.4
- Cinema: o quadro de seleção das capas era cortado (a rolagem encostava o pôster na borda e o ScrollViewer cortava em cima/embaixo). Agora a fileira tem margem interna e rola suavemente por conta própria (OnCinemaBringIntoView) deixando folga dos dois lados.

## v1.9.5
- "Tamanho dos cards" (Pequeno/Médio/Grande) agora vale também para a tela inicial (Clássico, Cinema, Carrossel e Sala), além da biblioteca. O Painel já preenche a tela.
- Rolagem, legenda e brilho da home passaram a medir posições já com a escala aplicada.

## v1.9.6
- Cinema: a faixa de jogos parava de acompanhar o foco (o título mudava mas a rolagem ficava presa). O controle da animação usava um sinalizador que podia ficar travado; agora é baseado em tempo e se recupera sozinho.

## v1.9.7
- Cinema: a animação da faixa de jogos engasgava. Em vez de rolar o ScrollViewer (que refaz o layout a cada quadro), a faixa agora desliza com um TranslateTransform animado, que roda na thread de renderização.

## v1.9.8
- Cinema: o primeiro e o último jogo apareciam cortados/colados na borda porque o ScrollViewer rolava sozinho (o apresentador de conteúdo tratava o pedido de "trazer à vista" antes do nosso código, que estava no ScrollViewer). O tratamento agora fica na própria fileira, dentro do visor; folga lateral aumentada.

## v1.9.9
- Tela inicial: o papel de parede do jogo agora aparece na tela toda, como na Steam Big Picture. O degradê escuro que cobria a parte de baixo ficou bem mais leve (42 %) na home; nas outras páginas continua igual.

## v1.9.10
- Home: abaixo dos cards o papel de parede fica desfocado e um pouco mais escuro (como na Steam Big Picture), para as informações ficarem legíveis. O wallpaper segue nítido na parte de cima.

## v1.9.11
- Home: wallpaper levemente escurecido em toda a tela (28 %), desfoque abaixo dos cards bem mais leve (raio 12, antes 38) e degradê de fundo mais suave.

## v1.9.12
- Home: a faixa desfocada abaixo dos cards agora acompanha o zoom e o movimento lento do wallpaper (compartilha a mesma transformação), em vez de ficar parada.
- NOVIDADES / AMIGOS / RECOMENDADOS na home com texto branco (antes cinza).

## v1.9.13
- **Novos estilos da tela inicial:** Painel, Sala (OLED), Cinema e Carrossel, além do Clássico. Escolha em Configurações → Interface.
- **Personalização:** cor de destaque que vale para todo o sistema, tamanho dos cards (biblioteca e início) e relógio grande.
- **Contas da Steam:** escolha a conta ao abrir, veja só os jogos instalados dela e troque pela foto do perfil (a Steam em segundo plano acompanha).
- **Início mais bonito:** o papel de parede do jogo ocupa a tela toda, com um leve desfoque embaixo dos cards, como na Steam Big Picture.
- **Avisos e dicas:** alerta de bateria baixa e de download concluído, e dicas de botões que mudam conforme a tela.
- **Modo Xbox:** o botão Y abre a busca e o teclado de toque do Windows.
- **Correções:** navegação do controle (setas que exigiam dois toques), animação do Cinema e cortes nos cards.

## Notas técnicas v1.9.13
- Modo Xbox: o botão Y (Buscar) abre a busca e agora garante que o teclado de toque do Windows apareça: libera a abertura automática no registro (HKCU\Software\Microsoft\TabletTip\1.7), abre o TabTip e, se o painel não ficar visível, pede para ele aparecer pela interface COM ITipInvocation.
- Release: o texto "O que mudou" agora vem da seção "## v<versão>" deste arquivo (workflow release.yml). Créditos no README citam a ajuda do Claude.
- README: os prints foram substituídos por um único GIF de demonstração (docs/demo.gif); a pasta docs/screenshots foi removida.
- Correção do teste `Marks_only_the_chosen_account_as_most_recent` (CI): em .NET os grupos sem nome são numerados antes dos nomeados, então `Groups[4]` era o corpo e não a chave de fechamento. Agora usa grupos nomeados (head/body/tail).

## v1.9.14
- **Teclado próprio do launcher:** o botão Y (e A em uma caixa de texto) abre um teclado na tela feito pelo DeckLauncher, sem depender do teclado do Windows. A digita, X apaga, Y insere espaço e B fecha. Teclas Shift, Espaço, Limpar e Pronto; o teclado do computador também funciona.
- **Biblioteca:** ao trocar de aba (LB/RB) o seletor e a rolagem ficavam presos na lista anterior; agora são reiniciados.

## Notas técnicas v1.9.14
- Novo `MainWindow.Osk.cs` (teclado), estilo `OskKey` no Theme.xaml, painel `OskPanel` no MainWindow.xaml, `ResetLibrarySelector()` em ApplyGames. O teclado do Windows (TabTip) continua no código mas não é mais usado pela busca.

## v1.9.15
- **Teclado:** botão "abc/ABC" ao lado do espaço e o gatilho direito (RT) alternam maiúsculas fixas; o ⇧ continua valendo só para a próxima letra. A barra de dicas mostra RT MAIÚSCULA.
- **Biblioteca:** ao trocar de aba com LB/RB o seletor vai direto para o primeiro jogo da nova lista (em abas vazias não há seletor).

## v1.9.16
- Voltou para a base da v1.9.15 (a tentativa de jogos da Epic não instalados, v1.10.0, foi descartada; fica guardada em /home/claude/deck/discarded_v1.10.0).
- **Biblioteca:** a aba "Não instalados" foi removida. Os jogos da conta Steam que não estão instalados agora aparecem na aba **Steam**, depois dos instalados, esmaecidos (ficam mais nítidos quando selecionados). Instalar continua pela página do jogo. O número da aba Steam conta só os instalados.

## v1.10.0
- **Conta da Epic (como o Playnite):** em Configurações > Integrações, **Entrar na Epic** abre a página oficial de login da Epic no navegador. Depois de entrar, copie o texto que a Epic mostra (Ctrl+A, Ctrl+C); o launcher percebe sozinho o código (ou use **Colar código**) e lê a sua biblioteca. O launcher nunca vê nem guarda a senha, só a sessão, criptografada neste PC.
- **Jogos da Epic não instalados:** aparecem na aba **Epic** da biblioteca, depois dos instalados, esmaecidos e com capa da Epic. **Instalar** abre o Epic Games Launcher na sua biblioteca, onde você clica em Instalar.
- A biblioteca é atualizada ao abrir (no máximo a cada 6 horas) ou por **Atualizar biblioteca**. **Sair da conta** apaga a sessão e a lista. Jogos da EA e da Ubisoft ficam de fora, como no Playnite.

## Notas técnicas v1.10.0
- Novo `Core/Epic/EpicAccount.cs` (cliente OAuth do launcher da Epic, o mesmo do Playnite, leitura da biblioteca e do catálogo), `MainWindow.EpicAccount.cs` (login, sincronização e saída), `EpicAccountTests.cs`. `AppSettings` ganhou EpicTokens (DPAPI, fora do backup), EpicLibrary, EpicSkipped, EpicSyncedAt, EpicAccountName. `Game.CoverUrl` e `CoverDownloader` baixam a capa da Epic só de hosts da Epic.
- As chamadas à API da Epic não puderam ser testadas no ambiente de desenvolvimento.

## v1.10.1
- **Login da Epic sem copiar código:** **Entrar na Epic** agora abre a página oficial de login da Epic numa janela do próprio launcher (como o Playnite). Ao terminar o login a janela fecha sozinha e a biblioteca é lida. Se o Windows não tiver o WebView2 (já vem no Windows 10/11), abre no navegador e o método de copiar o código continua valendo.

## Notas técnicas v1.10.1
- Novo `EpicLoginWindow.cs` (WebView2, pasta de dados em %LocalAppData%\DeckLauncher\epic-login, cookies apagados antes e depois). Pacote Microsoft.Web.WebView2 1.0.2592.51 no csproj.

## v1.10.2
- **Epic, Instalar:** agora abre o Epic Games Launcher já no jogo, pedindo a instalação (link `com.epicgames.launcher://apps/...?action=install`), em vez de só abrir a biblioteca.
- **Epic, arte:** os jogos não instalados também ganham logotipo e papel de parede (além da capa), como os instalados. Baixam aos poucos, 25 jogos por vez; o logotipo pelo SteamGridDB precisa da sua chave.

## v1.10.3
- **Epic, Instalar:** se o Epic Games Launcher estiver fechado, o DeckLauncher abre ele primeiro, espera uns segundos e só então pede a instalação (antes o pedido se perdia).
- **Biblioteca mais rápida:** ao trocar de aba, os primeiros jogos aparecem na hora e o resto vai entrando em seguida, em vez de esperar a lista inteira (Steam e Epic com muitos jogos).

## v1.10.4
- Corrigido erro de build da v1.10.3 (Path sem namespace em MainWindow.GamePage.cs).

## v1.11.0
- **Sino de notificações:** novo ícone de sino ao lado da lupa. Enquanto capas, logos e wallpapers estão sendo baixados, o contador mostra quantos faltam (um só contador para tudo). Ao clicar, o painel mostra a barra de progresso e o total (capas, logos, wallpapers). Quando termina, aparece um aviso na tela e a notificação "Arte baixada: ..." fica no painel (contador = não lidas) até abrir ou limpar.

## Notas técnicas v1.11.0
- Novo `ArtProgress.cs` (`LibraryService.Art`, `ArtJob`) alimentado por DownloadMissingArtAsync e DownloadNotInstalledCoversAsync; `MainWindow.Notifications.cs`, `NotifPanel` no QuickLayer.

## v1.11.1
- Corrigido erro de build da v1.11.0 (método de arte da Epic removido por engano).

## v1.11.2
- **Um só sino:** o sino que já existia (antes "Downloads") agora é o das notificações, com o contador. Clicar abre o painel; **Mais detalhes (Downloads)** abre a janela de Downloads.
- **Jogos baixando:** downloads da Steam em andamento aparecem no painel (nome, barra e porcentagem) e entram no contador; ao começar um download aparece um aviso, e quando termina fica a notificação "terminou de baixar".

## v1.11.3
- **Downloads:** o progresso do download de capas, logos e wallpapers também aparece na página Downloads (cartão "Baixando artes" com porcentagem e contagem), igual ao do sino.

## v1.11.4
- **Artes:** o sino e a página Downloads agora mostram o andamento de cada tipo ("Capas 3/10 · Logos 5/17 · Wallpapers 7/17"), em vez de quantas foram salvas (que ficava em 0 quando nada era encontrado). O resumo final continua dizendo quantas foram baixadas.

## v1.12.0
- **Epic, arte oficial:** capa, imagem larga (wallpaper) e logotipo agora vêm do catálogo da própria Epic, para os jogos instalados e não instalados, sem precisar de chave. SteamGridDB e Wallhaven ficam só como reserva. Na primeira sincronização depois de atualizar, a lista da Epic é lida de novo uma vez para pegar essas imagens.
- **Epic, instalação no sino:** ao pedir Instalar, o jogo aparece no sino como "Instalando pela Epic" (barra sem porcentagem, a Epic não informa o andamento) e entra no contador. Quando o jogo aparece como instalado, há aviso e notificação "terminou de instalar".
- **Atualizações no sino:** Epic: avisa quando um jogo instalado tem build mais novo (checa no máximo a cada 3 horas, cada build é avisada uma vez). Steam: avisa quando uma atualização começa.
- **Revisão geral:** conferência estática do código (usings, eventos do XAML, chaves de recursos, métodos duplicados) e testes novos (imagens do catálogo, builds da Epic, versão instalada, hosts permitidos).

## Notas técnicas v1.12.0
- Core: `EpicLibraryGame` ganhou WideUrl, LogoUrl e ArtChecked; `Game` ganhou WideUrl, LogoUrl e Version; `EpicAccountClient.GetLatestBuildsAsync/ParseBuilds`; `LogoDownloader.DownloadFromEpicAsync`; `CoverDownloader` usa a URL por tipo (HasStoreArt(game, kind)).
- App: `LibraryService.CheckEpicUpdatesAsync`; `MainWindow.Notifications.cs` com instalações da Epic pendentes e avisos de atualização.
- A comparação de build da Epic usa AppVersionString do manifesto contra buildVersion da API de assets (como o Legendary); não pôde ser testada aqui.

## v1.13.0
- **Conquistas:** nova aba **CONQUISTAS** na página do jogo, entre SOBRE e INFORMAÇÕES DO JOGO (LB/RB passam por ela). Mostra quantas você desbloqueou (barra e porcentagem), cada conquista com ícone, nome, descrição e a data de desbloqueio; as desbloqueadas vêm primeiro.
- **Steam:** lê as conquistas pelo seu perfil público da Steam (sem chave e sem login). Se aparecer "As conquistas estão ocultas", no perfil da Steam deixe **Detalhes do jogo** como **Público**.
- **Epic (experimental):** usa a conta da Epic conectada em Configurações > Integrações. A Epic não documenta esse serviço; se ele não responder, a aba explica o motivo (ou mostra só a lista, sem o seu progresso).
- Conquistas ocultas aparecem como "Conquista oculta" até serem desbloqueadas. Os resultados ficam guardados por 30 minutos.

## Notas técnicas v1.13.0
- Core: `GameAchievements/GameAchievements.cs` (`Achievement`, `AchievementSet`, `SteamAchievements` reaproveita o leitor `Achievements` que já existia (página pública da comunidade e, se houver chave salva, a Web API), `EpicAchievementsClient` com GraphQL `store.epicgames.com/graphql`, Bearer do login da Epic, sandboxId = CatalogNamespace). Ícones só de hosts da Steam e da Epic. App: `MainWindow.Achievements.cs`, `LibraryService.FreshEpicTokensAsync`. Testes em `GameAchievementsTests.cs`.
- As consultas GraphQL da Epic seguem os nomes de campos publicados por terceiros e não puderam ser testadas aqui.

## v1.13.1
- Corrigido o erro de build da v1.13.0 (nome "Achievements" já existia no Core). A aba Conquistas agora reaproveita o leitor da Steam que já existia, que também usa a chave da Web API da Steam quando ela estiver salva.

## v1.13.2
- **Cinema:** dos botões Jogar/Biblioteca/Aplicativos/Amigos, apertar para cima agora vai para as opções do topo (notificações, rede, som e perfil). Antes ficava preso.

## v1.13.3
- **Troca de conta:** os nomes das contas agora aparecem em branco, legíveis.
- **Todos os jogos nas duas contas:** o launcher mostra todos os jogos Steam instalados no PC, seja qual for a conta que instalou. Antes cada conta via só os seus. Para jogar um jogo que não está na biblioteca da conta em uso, a Steam precisa permitir (compartilhamento em família ou licença da própria conta).

## v1.0.4
- **Biblioteca da Epic:** entre na sua conta da Epic em Configurações > Integrações (login oficial numa janela do launcher) e veja na aba Epic os jogos que você tem e ainda não instalou; Instalar abre o Epic Games Launcher já no jogo. Capas, wallpapers e logotipos vêm da própria Epic.
- **Sino de notificações:** um só contador para as artes sendo baixadas, jogos baixando na Steam, instalações da Epic e atualizações, com botão "Mais detalhes" que abre a janela de Downloads.
- **Conquistas:** nova aba na página do jogo (Steam pelo perfil público; Epic de forma experimental).
- **Contas Steam:** os jogos instalados no PC aparecem em todas as contas; os nomes na tela de troca de conta ficaram legíveis.
- **Mais rápido:** troca de abas, carregamento da biblioteca e das imagens mais leves; capas, logos e wallpapers que não existem deixam de ser procurados a cada abertura (a próxima tentativa é só depois de 3 dias).
- **Cinema:** dos botões Jogar/Biblioteca/Aplicativos/Amigos dá para subir às opções do topo.
- **Bluetooth no painel de Som:** liga/desliga o Bluetooth, adiciona fones sem fio já pareados no Windows (ou abre o pareamento do Windows para um novo), guarda numa lista e conecta ao clicar ou automaticamente quando o fone estiver ligado por perto.

## Notas técnicas v1.0.4
- Versão do aplicativo ajustada para 1.0.4 (mostrada em Configurações > Sobre).
- Bluetooth: `BluetoothNative` (App) usa a API Win32 `bthprops.cpl` para listar pareados, conectar (serviços A2DP/Hands-free/Headset) e desconectar; o rádio liga/desliga via WinRT dentro de um PowerShell oculto (sem mudar o framework do projeto). Lista salva em `AppSettings.BluetoothDevices` (`SavedBluetoothDevice`, endereço de 12 hex) e `BluetoothAutoConnect`; tentativa automática ao abrir e a cada 45 s com a janela ativa; quem foi desconectado à mão não reconecta até reiniciar.
- `ArtTriedStore` (art_tried.json na pasta de capas): memória em disco das artes já tentadas, validade de 3 dias; limpar o cache de arte também a limpa. Downloads de arte com 4 em paralelo; decodificação de imagens limitada a 2-4 ao mesmo tempo; leitura das artes em disco fora da thread da interface; a lista de não instalados aparece antes da sincronização da Epic.

## v1.0.5
- **Idiomas:** português, inglês e espanhol. Em Configurações > Interface > Idioma abre uma janela pequena para escolher Automático (segue o Windows), English, Português ou Español; textos das telas, avisos e notificações são traduzidos na hora.
- **Desinstalar:** botão DESINSTALAR na página do jogo (ao lado de VER NA LOJA) para jogos instalados da Steam e da Epic. Abre a Steam já perguntando se quer remover; na Epic abre a Biblioteca do Epic Games Launcher (a Epic não tem link direto de desinstalação), onde o menu (⋯) do jogo tem Desinstalar. Quando o jogo some do disco, o launcher o tira de todas as listas, avisa no sino e volta para a aba da loja, onde ele aparece esmaecido para instalar de novo.
- **Lojas:** a página mostra só a Steam e/ou o Epic Games Launcher que estiverem instalados no PC; sem nenhum, aparece um aviso.
- **Temas legíveis:** os nomes dos estilos de tela inicial (Configurações > Interface) ficavam pretos e sumiam no tema Preto OLED; agora são brancos.
- **Idioma:** o botão abre uma janelinha para escolher Automático, English, Português ou Español.

## Notas técnicas v1.0.5
- Idiomas: `Loc` (Core) já existia com tabelas de chave em inglês usadas pelo Core; as telas do App são escritas em português, então `AppTexts` (Core, `AppTexts.Data.cs` com ~520 linhas português→inglês/espanhol; `{0}` marca valores, `*` libera qualquer texto) traduz textos exatos e padrões. `UiText` (App) percorre a árvore visual (TextBlock, Content de botões, ToolTip) lembrando o texto original de cada elemento, então dá para trocar de idioma várias vezes; roda a cada mudança de layout (com 80 ms de espera) fora do português. Padrões com poucas letras fixas só aceitam números/tamanhos, para nunca alterar títulos de jogos. `AppSettings.Language` (null = Automático). Textos novos: acrescentar uma linha em `AppTexts.Data.cs`.
- Versão do aplicativo ajustada para 1.0.5 (mostrada em Configurações > Sobre).
- `StyleCard` (Theme.xaml) agora define `Foreground` branco: os textos dos cartões herdavam a cor padrão do botão.
- `MainWindow.Uninstall.cs`: `steam://uninstall/<id>` para Steam; Epic abre `com.epicgames.launcher://store/library`; `WatchUninstall` confere o disco por até 15 minutos e atualiza as listas.
