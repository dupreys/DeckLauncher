namespace DeckLauncher.Core;

/// <summary>
/// A game from any provider. <see cref="Source"/> + <see cref="Id"/> is the unique key.
/// </summary>
public sealed record Game(
    string Source,
    string Id,
    string Name,
    DateTimeOffset? LastPlayed = null,
    long SizeOnDisk = 0)
{
    /// <summary>Absolute path to a cover image on disk, if one was found.</summary>
    public string? CoverPath { get; init; }

    /// <summary>Absolute path to a wide background image (hero art), if one was found.</summary>
    public string? BackdropPath { get; init; }

    /// <summary>Transparent game logo shown instead of the title, if one was chosen.</summary>
    public string? LogoPath { get; init; }

    /// <summary>The name the store reports, before any rename by the user.</summary>
    public string? OriginalName { get; init; }

    /// <summary>The game's main executable, when the store tells us (used to know when it is running).</summary>
    public string? ExecutablePath { get; init; }

    /// <summary>Folder the game is installed in (used to find its processes when it must be force-closed).</summary>
    public string? InstallPath { get; init; }

    /// <summary>Steam: SteamID64 of the account that last used the installation (the manifest's LastOwner).</summary>
    public string? Owner { get; init; }

    /// <summary>True for a game the user owns but has not installed (listed under "Not installed"; it can be downloaded).</summary>
    public bool NotInstalled { get; init; }

    /// <summary>Epic games not installed: the address of the cover in Epic's catalog (an https link on Epic's own servers).</summary>
    public string? CoverUrl { get; init; }

    /// <summary>Epic: the wide picture and the logo in Epic's catalog (https links on Epic's own servers).</summary>
    public string? WideUrl { get; init; }
    public string? LogoUrl { get; init; }

    /// <summary>Epic: the installed build (AppVersionString of the manifest), to tell when an update exists.</summary>
    public string? Version { get; init; }

    // Epic catalog ids (needed for launch and store links)
    public string? CatalogNamespace { get; init; }
    public string? CatalogItemId { get; init; }

    /// <summary>GOG: the name in the store address (gog.com/game/&lt;slug&gt;), for the store link of a game that is not installed.</summary>
    public string? Slug { get; init; }
}
