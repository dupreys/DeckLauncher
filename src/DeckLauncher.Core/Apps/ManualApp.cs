namespace DeckLauncher.Core.Apps;

/// <summary>A program (Path) or a website (Url) the user added to the Apps tab.</summary>
public sealed class ManualApp
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    /// <summary>Set for website shortcuts; opens in the default browser.</summary>
    public string Url { get; set; } = "";

    public bool IsWeb => !string.IsNullOrWhiteSpace(Url);

    /// <summary>The identity used for duplicates and for the saved order.</summary>
    public string Key => IsWeb ? Url : Path;
}
