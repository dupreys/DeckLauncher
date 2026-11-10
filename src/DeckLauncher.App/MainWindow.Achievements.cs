using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DeckLauncher.Core;
using DeckLauncher.Core.GameAchievements;
using DeckLauncher.Core.Epic;

namespace DeckLauncher.App;

// "CONQUISTAS" tab of the game page: Steam (public profile) and Epic (the connected account).
public partial class MainWindow
{
    private readonly Dictionary<string, (DateTime At, AchievementSet Set)> _achCache = new();
    private CancellationTokenSource? _achCts;
    private string? _achShownKey;

    private static string AchKey(Game game) => $"{game.Source}:{game.Id}";

    /// <summary>A game was opened: the list is loaded only if the tab is opened.</summary>
    private void ResetAchievements()
    {
        _achCts?.Cancel();
        _achShownKey = null;
        GameAchList.Children.Clear();
        GameAchSummary.Text = "";
        GameAchBar.Visibility = Visibility.Collapsed;
    }

    private async Task ShowAchievementsAsync(GameItem item)
    {
        var game = item.Game;
        var key = AchKey(game);
        if (_achShownKey == key) return;

        _achCts?.Cancel();
        var cts = _achCts = new CancellationTokenSource();
        if (_achCache.TryGetValue(key, out var cached) && DateTime.UtcNow - cached.At < TimeSpan.FromMinutes(30))
        {
            RenderAchievements(cached.Set);
            _achShownKey = key;
            return;
        }

        GameAchList.Children.Clear();
        GameAchBar.Visibility = Visibility.Collapsed;
        GameAchSummary.Text = "Carregando conquistas…";

        AchievementSet set;
        try
        {
            if (game.Source == "Steam")
                set = await SteamAchievements.FetchAsync(LibraryService.Http, game, SteamIdValue(), App.Settings.SteamWebApiKey, cts.Token);
            else if (game.Source == "Epic")
            {
                var tokens = await _library.FreshEpicTokensAsync();
                set = await new EpicAchievementsClient(LibraryService.Http).FetchAsync(tokens, game.CatalogNamespace, "pt-BR", cts.Token);
            }
            else set = AchievementSet.Failed("source");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or EpicSignInException)
        {
            set = AchievementSet.Failed("fetch");
        }

        if (cts.IsCancellationRequested || !ReferenceEquals(_gameItem, item)) return;
        if (set.Problem is null or "noprogress" or "none") _achCache[key] = (DateTime.UtcNow, set);
        RenderAchievements(set);
        _achShownKey = key;
    }

    private static string? AchievementProblemText(string? problem) => problem switch
    {
        "noid" => "Não achei a sua conta da Steam para ler as conquistas.",
        "private" => "Não consegui ler as conquistas. Se o seu perfil da Steam estiver privado, deixe \"Detalhes do jogo\" como Público e tente de novo.",
        "none" => "Este jogo não tem conquistas.",
        "signin" => "Entre na sua conta da Epic em Configurações > Integrações para ver as conquistas.",
        "source" => "Conquistas só estão disponíveis para jogos da Steam e da Epic.",
        "fetch" => "Não consegui carregar as conquistas agora. Tente de novo mais tarde.",
        _ => null,
    };

    private void RenderAchievements(AchievementSet set)
    {
        GameAchList.Children.Clear();
        if (set.Items.Count == 0)
        {
            GameAchBar.Visibility = Visibility.Collapsed;
            GameAchSummary.Text = AchievementProblemText(set.Problem) ?? "Sem conquistas para mostrar.";
            return;
        }

        var unlocked = set.UnlockedCount;
        if (set.Problem == "noprogress")
        {
            GameAchBar.Visibility = Visibility.Collapsed;
            GameAchSummary.Text = $"{set.Items.Count} conquistas. A Epic não liberou o seu progresso agora, então só a lista aparece.";
        }
        else
        {
            GameAchBar.Visibility = Visibility.Visible;
            GameAchBar.Value = (double)unlocked / set.Items.Count;
            GameAchSummary.Text = $"{unlocked} de {set.Items.Count} conquistas · {(int)Math.Round(100.0 * unlocked / set.Items.Count)}%";
        }

        foreach (var achievement in set.Items.Take(300)) GameAchList.Children.Add(BuildAchievementRow(achievement, set.Problem != "noprogress"));
    }

    private Button BuildAchievementRow(Achievement a, bool showState)
    {
        var icon = new Image { Width = 52, Height = 52, Stretch = Stretch.UniformToFill, VerticalAlignment = VerticalAlignment.Center, Opacity = a.Unlocked || !showState ? 1 : 0.7 };
        _ = SetIconAsync(icon, a.IconUrl);

        var name = a.Hidden ? "Conquista oculta" : a.Name;
        var text = new StackPanel { Margin = new Thickness(16, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = name, FontSize = 18, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock
        {
            Text = a.Hidden ? "Continue jogando para descobrir." : a.Description ?? "",
            FontSize = 14, Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis,
            Visibility = a.Hidden || !string.IsNullOrEmpty(a.Description) ? Visibility.Visible : Visibility.Collapsed,
        });

        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        if (showState && a.Unlocked)
            right.Children.Add(new TextBlock { Text = a.UnlockedAt is { } at ? $"Desbloqueada em {at.LocalDateTime:dd/MM/yyyy}" : "Desbloqueada", FontSize = 13, Opacity = 0.7, HorizontalAlignment = HorizontalAlignment.Right });
        if (a.RarityPercent is { } rarity)
            right.Children.Add(new TextBlock { Text = $"{rarity:0.#}% dos jogadores", FontSize = 13, Opacity = 0.7, HorizontalAlignment = HorizontalAlignment.Right });

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(icon);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        var row = new Button
        {
            Style = (Style)FindResource("PowerItem"),
            Height = 76,
            Margin = new Thickness(0, 0, 0, 6),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = grid,
            Opacity = !showState || a.Unlocked ? 1 : 0.85,
        };
        row.GotKeyboardFocus += (_, _) => row.BringIntoView();
        return row;
    }

    private static async Task SetIconAsync(Image image, string? url)
    {
        if (await RemoteImage.LoadAsync(LibraryService.Http, url, 104) is { } source) image.Source = source;
    }
}
