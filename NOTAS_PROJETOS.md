# Notas dos projetos (leitura obrigatória ao retomar)

## LiteLauncher — FECHADO na v59 (31/10/2026)
- Entrega final: /home/claude/v6/LiteLauncher_v59.zip (fonte em /home/claude/v6/LiteLauncher). Backups: v21, v36, v53, v54..v58.
- Stack: C#/.NET 8 WPF; Core (net8.0) + App (WPF) + testes xunit. Instalador Inno Setup via GitHub Actions ao empurrar tag. v1.0.0 já publicada; mudanças pendentes sugerem tag 1.1.0 (editar RELEASE_NOTES.md antes).
- Funções: Steam/Epic/manuais/Apps do menu Iniciar, controle/teclado, downloads Steam, mute do jogo no overlay, SteamGridDB (capas, logos, capturas), fundos 16:9 automáticos (captura da loja Steam -> Wallhaven com checagem de tags), aba "Fundos 16:9" (Wallhaven) em Gerenciar. IGDB foi removido. SteamGridDB só para capas (e picker manual).
- Nunca foi compilado aqui (sem dotnet): o usuário compila. Chaves nunca no repositório (DPAPI no settings.json).
- Roadmap NÃO iniciado (perguntar antes): provedores GOG/Xbox, remapeamento de controle, atualização automática, assinatura de código.
- Regras: responder em português; sem marcas/arte Xbox/Steam/Valve; avisar sobre RELEASE_NOTES.md antes de tag.

## Projeto novo — launcher com visual do Steam OS / Big Picture (iniciado 31/10/2026)
- Pasta: /home/claude/deck (solução própria). Reaproveita o Core do LiteLauncher v59 (namespaces LiteLauncher.Core mantidos de propósito; renomear só com compilador à mão).
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
