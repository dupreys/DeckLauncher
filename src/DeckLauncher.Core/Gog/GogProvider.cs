using System.Diagnostics;
using Microsoft.Win32;

namespace DeckLauncher.Core.Gog;

/// <summary>
/// GOG games installed on this PC, read from the registry keys GOG's installers and GOG Galaxy write
/// (HKLM\SOFTWARE\WOW6432Node\GOG.com\Games\&lt;id&gt;). No GOG account is needed. The games are DRM-free, so they start straight from their .exe.
/// </summary>
public sealed class GogProvider : IGameProvider
{
    public const string ProviderName = "GOG";
    private const string GamesKey = @"SOFTWARE\GOG.com\Games";

    private readonly Func<IReadOnlyList<IReadOnlyDictionary<string, string?>>> _readEntries;

    public GogProvider() : this(ReadRegistry) { }

    /// <summary>For tests: supplies the registry entries (one dictionary of values per game).</summary>
    public GogProvider(Func<IReadOnlyList<IReadOnlyDictionary<string, string?>>> readEntries) => _readEntries = readEntries;

    public string Name => ProviderName;

    public bool IsAvailable => true;

    public Task<IReadOnlyList<Game>> GetGamesAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<Game>>(() =>
        {
            var games = new Dictionary<string, Game>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in _readEntries())
            {
                if (ParseEntry(entry) is { } game && game.ExecutablePath is { } exe && File.Exists(exe))
                    games[game.Id] = game;
            }
            return games.Values.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }, cancellationToken);

    /// <summary>Turns one registry entry into a game, or null for DLC, tools without a program and broken entries.</summary>
    public static Game? ParseEntry(IReadOnlyDictionary<string, string?> values)
    {
        string? Get(string name) => values.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

        var id = Get("gameID") ?? Get("_key");
        var name = Get("gameName");
        if (id is null || name is null) return null;
        if (!id.All(char.IsAsciiDigit)) return null;
        if (Get("dependsOn") is { } baseGame && baseGame != id) return null; // DLC of another game

        var folder = Get("path");
        var exe = Get("exe");
        if (exe is null && folder is not null && Get("exeFile") is { } file) exe = Path.Combine(folder, file);
        if (exe is null || !Path.IsPathFullyQualified(exe) || !string.Equals(Path.GetExtension(exe), ".exe", StringComparison.OrdinalIgnoreCase)) return null;

        return new Game(ProviderName, id, name)
        {
            OriginalName = name,
            ExecutablePath = exe,
            InstallPath = folder ?? Path.GetDirectoryName(exe),
            Version = Get("ver"),
        };
    }

    /// <summary>The extra arguments the entry asks for (GOG's launchParam), kept to what a game's own shortcut would carry.</summary>
    public static string? LaunchArguments(IReadOnlyDictionary<string, string?> values) =>
        values.TryGetValue("launchParam", out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

    public void Launch(Game game)
    {
        if (game.Source != ProviderName) throw new ArgumentException("Not a GOG game.", nameof(game));

        var exe = game.ExecutablePath;
        if (string.IsNullOrWhiteSpace(exe) || !Path.IsPathFullyQualified(exe) || !File.Exists(exe))
            throw new FileNotFoundException("The game's .exe was not found.", exe);

        var entry = _readEntries().FirstOrDefault(e => e.TryGetValue("gameID", out var id) && id == game.Id);
        var start = new ProcessStartInfo(exe)
        {
            UseShellExecute = true,
            WorkingDirectory = entry is not null && entry.TryGetValue("workingDir", out var dir) && Directory.Exists(dir) ? dir : Path.GetDirectoryName(exe)!,
        };
        if (entry is not null && LaunchArguments(entry) is { } args) start.Arguments = args;
        Process.Start(start)?.Dispose();
    }

    /// <summary>The game's own uninstaller (GOG installs unins000.exe next to the game); null when it is not there.</summary>
    public static string? FindUninstaller(Game game)
    {
        if (string.IsNullOrEmpty(game.InstallPath) || !Directory.Exists(game.InstallPath)) return null;
        try
        {
            return Directory.EnumerateFiles(game.InstallPath, "unins*.exe", SearchOption.TopDirectoryOnly).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    public static IReadOnlyList<IReadOnlyDictionary<string, string?>> ReadRegistry()
    {
        var list = new List<IReadOnlyDictionary<string, string?>>();
        if (!OperatingSystem.IsWindows()) return list;

        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var games = root.OpenSubKey(GamesKey);
                if (games is null) continue;
                foreach (var subName in games.GetSubKeyNames())
                {
                    using var key = games.OpenSubKey(subName);
                    if (key is null) continue;
                    var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["_key"] = subName };
                    foreach (var valueName in key.GetValueNames())
                        values[valueName] = key.GetValue(valueName)?.ToString();
                    list.Add(values);
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { /* not readable: no GOG games from this view */ }
        }
        return list;
    }
}
