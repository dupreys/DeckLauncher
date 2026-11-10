using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace DeckLauncher.Core;

/// <summary>
/// Translates the launcher's own screen texts. The source language is Portuguese (what the screens are written in);
/// <see cref="T"/> returns the English or Spanish text for <see cref="Loc.Language"/>, or the text itself when there is none.
/// Texts with values ("{0} jogos") are matched as patterns, so a toast built with string interpolation is translated too.
/// </summary>
public static partial class AppTexts
{
    private sealed record Pattern(Regex Regex, string English, string Spanish, int Groups);

    private static readonly Dictionary<string, (string English, string Spanish)> Exact = new(StringComparer.Ordinal);
    private static readonly List<Pattern> Patterns = new();
    private static readonly ConcurrentDictionary<string, string> CacheEn = new();
    private static readonly ConcurrentDictionary<string, string> CacheEs = new();

    // Patterns with few fixed letters ("{0} de {1}") only take numbers and sizes, so a game title never gets "translated".
    private static readonly Regex NumberLike = new(@"^[\d.,:/%\s]+(\s?(h|min|MB|GB|KB|B|Mb/s))?$", RegexOptions.Compiled);
    private static readonly Regex Placeholder = new(@"\\\{(\d)\}", RegexOptions.Compiled);
    private static readonly Regex Fill = new(@"\{(\d)\}", RegexOptions.Compiled);

    static AppTexts()
    {
        foreach (var row in Rows)
        {
            var free = row[0].StartsWith('*');
            var pt = free ? row[0][1..] : row[0];
            if (!pt.Contains('{')) { Exact[pt] = (row[1], row[2]); continue; }

            var letters = Fill.Replace(pt, "").Count(char.IsLetter);
            var numbersOnly = !free && letters < 7;
            var groups = Fill.Matches(pt).Count;
            var expression = "^" + Placeholder.Replace(Regex.Escape(pt), m => $"(?<g{m.Groups[1].Value}>.+?)") + "$";
            Patterns.Add(new Pattern(new Regex(expression, RegexOptions.Singleline | RegexOptions.Compiled), row[1], row[2], numbersOnly ? -groups : groups));
        }
        Patterns.Sort((a, b) => Literal(b).CompareTo(Literal(a)));
    }

    private static int Literal(Pattern p) => p.Regex.ToString().Length;

    /// <summary>The Portuguese source texts (used by the tests).</summary>
    public static IEnumerable<string[]> AllRows => Rows;

    public static string T(string? text) => T(text, Loc.Language);

    /// <summary>The text in the given language code ("en", "es" or "pt-BR").</summary>
    public static string T(string? text, string language)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        if (language is not ("en" or "es")) return text;
        var cache = language == "es" ? CacheEs : CacheEn;
        if (cache.TryGetValue(text, out var cached)) return cached;
        var result = Translate(text, language == "es");
        if (cache.Count > 3000) cache.Clear();
        cache[text] = result;
        return result;
    }

    private static string Translate(string text, bool spanish)
    {
        if (Exact.TryGetValue(text, out var hit)) return spanish ? hit.Spanish : hit.English;

        // The same text with spaces around it (" jogou X", "Nome:  ").
        var trimmed = text.Trim();
        if (trimmed.Length > 0 && trimmed != text && Exact.TryGetValue(trimmed, out hit))
        {
            var target = spanish ? hit.Spanish : hit.English;
            return text[..(text.Length - text.TrimStart().Length)] + target + text[text.TrimEnd().Length..];
        }

        foreach (var pattern in Patterns)
        {
            var match = pattern.Regex.Match(text);
            if (!match.Success) continue;
            var numbersOnly = pattern.Groups < 0;
            var values = new string[Math.Abs(pattern.Groups)];
            var ok = true;
            for (var i = 0; i < values.Length; i++)
            {
                var value = match.Groups[$"g{i}"].Value;
                if (numbersOnly && !NumberLike.IsMatch(value)) { ok = false; break; }
                values[i] = Part(value, spanish);
            }
            if (!ok) continue;
            var template = spanish ? pattern.Spanish : pattern.English;
            return Fill.Replace(template, m => int.Parse(m.Groups[1].Value) < values.Length ? values[int.Parse(m.Groups[1].Value)] : m.Value);
        }
        return text;
    }

    /// <summary>A value inside a text: translated as a whole, or piece by piece when it is a list ("2 capas, 1 logo").</summary>
    private static string Part(string value, bool spanish)
    {
        var language = spanish ? "es" : "en";
        var whole = T(value, language);
        if (whole != value || !value.Contains(", ")) return whole;
        return string.Join(", ", value.Split(", ").Select(part => T(part, language)));
    }
}
