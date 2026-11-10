using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using DeckLauncher.Core;

namespace DeckLauncher.App;

// Interface language: Automatic (follows Windows), English, Portuguese, Spanish.
public partial class MainWindow
{
    private readonly DispatcherTimer _uiTextTimer = new() { Interval = TimeSpan.FromMilliseconds(80) };

    private void InitLanguage()
    {
        _uiTextTimer.Tick += (_, _) =>
        {
            _uiTextTimer.Stop();
            if (Loc.Language != "pt-BR") UiText.Apply(this);
        };
        // Texts the program sets while it runs (toasts, lists, status lines) are translated shortly after they appear.
        LayoutUpdated += (_, _) =>
        {
            if (Loc.Language != "pt-BR" && !_uiTextTimer.IsEnabled) _uiTextTimer.Start();
        };
        UpdateLanguageButton();
        UiText.Apply(this, includeHidden: true);
    }

    private void UpdateLanguageButton()
    {
        var chosen = App.Settings.Language;
        LanguageButton.Content = chosen is null
            ? UiText.T("Automático") + " (" + Loc.NameOf(Loc.Language) + ")"
            : Loc.NameOf(chosen);
    }

    private void OnLanguageClick(object sender, RoutedEventArgs e)
    {
        var chosen = App.Settings.Language;
        LangAutoButton.Content = Mark(chosen is null) + UiText.T("Automático") + " (" + Loc.NameOf(Loc.Detect(CultureInfo.CurrentUICulture)) + ")";
        LangEnButton.Content = Mark(chosen == "en") + Loc.NameOf("en");
        LangPtButton.Content = Mark(chosen == "pt-BR") + Loc.NameOf("pt-BR");
        LangEsButton.Content = Mark(chosen == "es") + Loc.NameOf("es");
        LanguagePanel.Visibility = Visibility.Visible;
        (chosen switch { "en" => LangEnButton, "pt-BR" => LangPtButton, "es" => LangEsButton, _ => LangAutoButton }).Focus();
    }

    private static string Mark(bool selected) => selected ? "●  " : "     ";

    private void CloseLanguagePicker()
    {
        LanguagePanel.Visibility = Visibility.Collapsed;
        LanguageButton.Focus();
    }

    private void OnLanguageDimClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => CloseLanguagePicker();

    private void OnLanguageCancelClick(object sender, RoutedEventArgs e) => CloseLanguagePicker();

    private void OnLanguageChoiceClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string tag }) return;
        var next = tag == "auto" ? null : tag;
        App.Settings.Language = next;
        try { App.Store.Save(App.Settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        Loc.Set(next ?? Loc.Detect(CultureInfo.CurrentUICulture));
        UpdateLanguageButton();
        UiText.Apply(this, includeHidden: true);
        CloseLanguagePicker();
        UpdateHints(force: true);
    }
}
