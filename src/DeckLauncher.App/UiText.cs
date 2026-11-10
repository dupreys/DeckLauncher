using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using DeckLauncher.Core;

namespace DeckLauncher.App;

/// <summary>
/// Puts the interface texts in the chosen language. The screens are written in Portuguese; <see cref="AppTexts"/> holds the
/// English and Spanish versions. <see cref="Apply"/> walks the visible screen and swaps TextBlock texts, button contents and
/// tooltips, remembering the original text of each element so the language can be changed back and forth at any time.
/// </summary>
internal static class UiText
{
    private sealed class Memory : Dictionary<DependencyProperty, (string Original, string Shown)> { }

    private static readonly ConditionalWeakTable<DependencyObject, Memory> Remembered = new();

    public static string T(string? text) => AppTexts.T(text);

    /// <summary>Culture for numbers and dates in the current language.</summary>
    public static CultureInfo Culture => Loc.Language switch
    {
        "en" => CultureInfo.GetCultureInfo("en-US"),
        "es" => CultureInfo.GetCultureInfo("es-ES"),
        _ => CultureInfo.GetCultureInfo("pt-BR"),
    };

    /// <summary>A Portuguese/Spanish date format ("d 'de' MMMM") adjusted for English ("d MMMM").</summary>
    public static string DateFormat(string format) => Loc.Language == "en" ? format.Replace(" 'de' ", " ") : format;

    public static void Apply(DependencyObject root, bool includeHidden = false)
    {
        if (!includeHidden && root is UIElement { Visibility: not Visibility.Visible }) return;

        switch (root)
        {
            case TextBlock text:
                if (text.Inlines.Count <= 1 && !BindingOperations.IsDataBound(text, TextBlock.TextProperty))
                    Translate(text, TextBlock.TextProperty);
                if (text.ToolTip is string) Translate(text, FrameworkElement.ToolTipProperty);
                return;
            case ContentControl content:
                if ((content.Content is string || Remembered.TryGetValue(content, out var m) && m.ContainsKey(ContentControl.ContentProperty))
                    && !BindingOperations.IsDataBound(content, ContentControl.ContentProperty))
                    Translate(content, ContentControl.ContentProperty);
                if (content.ToolTip is string) Translate(content, FrameworkElement.ToolTipProperty);
                break;
            case FrameworkElement { ToolTip: string } element:
                Translate(element, FrameworkElement.ToolTipProperty);
                break;
        }

        if (root is Visual or System.Windows.Media.Media3D.Visual3D)
        {
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++) Apply(VisualTreeHelper.GetChild(root, i), includeHidden);
        }
    }

    private static void Translate(DependencyObject element, DependencyProperty property)
    {
        var current = element.GetValue(property) as string;
        if (string.IsNullOrEmpty(current)) return;

        var memory = Remembered.GetOrCreateValue(element);
        var original = memory.TryGetValue(property, out var known) && known.Shown == current ? known.Original : current;
        var translated = T(original);
        memory[property] = (original, translated);
        if (translated != current) element.SetValue(property, translated);
    }
}
