using System.Text.Json;

namespace DeckLauncher.Core.Gog;

/// <summary>A game the launcher itself downloaded from GOG (no GOG Galaxy registry entry exists for it).</summary>
public sealed class GogOwnInstall
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Folder { get; set; } = "";
    public string Exe { get; set; } = "";
    public string? Arguments { get; set; }
    public string? WorkingDir { get; set; }
    public string? Version { get; set; }

    /// <summary>The same values the GOG registry holds, so <see cref="GogProvider"/> reads both the same way.</summary>
    public IReadOnlyDictionary<string, string?> ToEntry() => new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
    {
        ["gameID"] = Id, ["gameName"] = Name, ["path"] = Folder, ["exe"] = Exe, ["launchParam"] = Arguments, ["workingDir"] = WorkingDir, ["ver"] = Version,
    };

    /// <summary>Reads goggame-&lt;id&gt;.info (written by GOG into every game folder): the main program and its arguments.</summary>
    public static GogOwnInstall? FromInfo(string folder, string id, string fallbackName, string? version)
    {
        var info = Path.Combine(folder, $"goggame-{id}.info");
        if (!File.Exists(info)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(info));
            var root = doc.RootElement;
            string? Str(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            if (!root.TryGetProperty("playTasks", out var tasks) || tasks.ValueKind != JsonValueKind.Array) return null;

            var candidates = tasks.EnumerateArray().Where(t => Str(t, "path") is { Length: > 0 } && (Str(t, "type") ?? "FileTask") == "FileTask").ToList();
            var task = candidates.FirstOrDefault(t => t.TryGetProperty("isPrimary", out var p) && p.ValueKind == JsonValueKind.True);
            if (task.ValueKind != JsonValueKind.Object) task = candidates.FirstOrDefault(t => (Str(t, "category") ?? "game") == "game");
            if (task.ValueKind != JsonValueKind.Object) return null;

            var exe = GogDownloader.SafePath(folder, Str(task, "path")!);
            if (exe is null || !File.Exists(exe)) return null;
            var work = Str(task, "workingDir") is { Length: > 0 } w ? GogDownloader.SafePath(folder, w) : null;
            return new GogOwnInstall
            {
                Id = id,
                Name = Str(root, "name") ?? fallbackName,
                Folder = folder,
                Exe = exe,
                Arguments = Str(task, "arguments"),
                WorkingDir = work is not null && Directory.Exists(work) ? work : null,
                Version = version,
            };
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return null; }
    }
}
