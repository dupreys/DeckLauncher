using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DeckLauncher.App;

// The bottom bar tells what the buttons do for the thing in focus: A changes its word (Abrir, Jogar, Alternar...)
// and, with a controller, X / Y / LB-RB show up when they do something here.
public partial class MainWindow
{
    private string _hintsSignature = "";

    private void UpdateHints(bool force = false)
    {
        var (select, extras) = DescribeFocus();
        var signature = select + "|" + string.Join(",", extras.Select(e => e.Label)) + "|" + _padHints;
        if (!force && signature == _hintsSignature) return;
        _hintsSignature = signature;

        SelectLabel.Text = select;
        HintExtras.Children.Clear();
        if (!_padHints) return; // the keyboard has no X / Y / bumpers
        foreach (var (key, color, label) in extras)
        {
            var text = new TextBlock { Foreground = System.Windows.Media.Brushes.Black, FontWeight = FontWeights.Bold, FontSize = 14 };
            var chip = new Border { Style = (Style)FindResource("KeyChip"), Child = text };
            SetChip(chip, text, true, key, key, color);
            if (key.Length > 1) { chip.Width = double.NaN; chip.Padding = new Thickness(10, 0, 10, 0); chip.CornerRadius = new CornerRadius(15); }
            HintExtras.Children.Add(chip);
            HintExtras.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.Bold, FontSize = 15, Margin = new Thickness(14, 0, 34, 0), VerticalAlignment = VerticalAlignment.Center });
        }
    }

    /// <summary>The word for A and the extra buttons that do something with the current focus.</summary>
    private (string Select, List<(string Key, string Color, string Label)> Extras) DescribeFocus()
    {
        var extras = new List<(string, string, string)>();
        if (OskOpen)
        {
            extras.Add(("X", "#2B7DE0", "APAGAR"));
            extras.Add(("Y", "#E0A800", "ESPAÇO"));
            extras.Add(("RT", "#9AA3AE", "MAIÚSCULA"));
            return ("DIGITAR", extras);
        }
        if (AccountOpen) return ("ESCOLHER", extras);
        if (MediaViewerOpen || PowerOpen || ManageOpen || OptionsOpen || ProfileOpen || LaunchOverlayOpen) return ("SELECIONAR", extras);

        var focused = Keyboard.FocusedElement as FrameworkElement;
        var overlay = MenuOpen || QuickOpen;
        var select = "SELECIONAR";

        if (!overlay && focused is { DataContext: GameItem item } && focused is Button)
        {
            select = item.IsRunning ? "VOLTAR AO JOGO" : "ABRIR";
            extras.Add(("X", "#2B7DE0", item.IsFavorite ? "DESFAVORITAR" : "FAVORITAR"));
        }
        else if (!overlay && ReferenceEquals(focused, PlayButton)) select = "JOGAR";
        else if (!overlay && focused is { Tag: AppEntry })
        {
            select = "ABRIR";
            extras.Add(("X", "#2B7DE0", "OPÇÕES"));
        }
        else if (focused is System.Windows.Controls.Primitives.ToggleButton) select = "ALTERNAR";

        if (!overlay)
        {
            if (_page is "Início" or "Biblioteca") extras.Add(("Y", "#E0A800", "BUSCAR"));
            if (_page is "Biblioteca" or "Feed" or "Configurações" or "Jogo") extras.Add(("LB RB", "#3C4350", "ABAS"));
        }
        return (select, extras);
    }
}
