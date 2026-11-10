using System.Text.RegularExpressions;
using DeckLauncher.Core;
using Xunit;

namespace DeckLauncher.Core.Tests;

public class AppTextsTests
{
    [Fact]
    public void Every_text_has_both_translations_with_the_same_placeholders()
    {
        foreach (var row in AppTexts.AllRows)
        {
            Assert.Equal(3, row.Length);
            Assert.All(row, text => Assert.False(string.IsNullOrWhiteSpace(text)));
            var want = Regex.Matches(row[0], @"\{\d\}").Select(m => m.Value).OrderBy(v => v);
            Assert.True(want.SequenceEqual(Regex.Matches(row[1], @"\{\d\}").Select(m => m.Value).OrderBy(v => v)), "English placeholders: " + row[0]);
            Assert.True(want.SequenceEqual(Regex.Matches(row[2], @"\{\d\}").Select(m => m.Value).OrderBy(v => v)), "Spanish placeholders: " + row[0]);
        }
    }

    [Theory]
    [InlineData("Configurações", "en", "Settings")]
    [InlineData("Configurações", "es", "Ajustes")]
    [InlineData("Configurações", "pt-BR", "Configurações")]
    [InlineData("Instalar", "en", "Install")]
    [InlineData("Texto que não existe na tabela", "en", "Texto que não existe na tabela")]
    public void Exact_texts_are_translated(string text, string language, string expected) =>
        Assert.Equal(expected, AppTexts.T(text, language));

    [Fact]
    public void Texts_with_values_are_translated_as_patterns()
    {
        Assert.Equal("Hades connected.", AppTexts.T("Hades conectado.", "en"));
        Assert.Equal("Hades connected via Bluetooth.", AppTexts.T("Hades conectado por Bluetooth.", "en"));
        Assert.Equal("12 games", AppTexts.T("12 jogos", "en"));
        Assert.Equal("Art downloaded: 2 covers, 1 logo.", AppTexts.T("Arte baixada: 2 capas, 1 logo.", "en"));
        Assert.Equal("Buenas tardes  ·  3 amigos en línea", AppTexts.T("Boa tarde  ·  3 amigos online", "es"));
        Assert.Equal("1,2 GB of 3,4 GB", AppTexts.T("1,2 GB de 3,4 GB", "en"));
        Assert.Equal("2 of 10 · Covers 1/4, Logos 1/6", AppTexts.T("2 de 10 · Capas 1/4, Logos 1/6", "en"));
    }

    [Theory]
    [InlineData("Dragon Age de Origins")]
    [InlineData("Halo 3 de Luxe")]
    [InlineData("Quake: Champions")]
    public void Game_titles_are_never_changed(string title)
    {
        Assert.Equal(title, AppTexts.T(title, "en"));
        Assert.Equal(title, AppTexts.T(title, "es"));
    }

    [Fact]
    public void Surrounding_spaces_are_kept() => Assert.Equal(" played Hades", AppTexts.T(" jogou Hades", "en"));
}
