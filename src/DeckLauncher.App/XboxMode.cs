using System.IO;
using DeckLauncher.Core.Settings;

namespace DeckLauncher.App;

/// <summary>
/// Modo Xbox (Experiência de Tela Cheia do Xbox): o programa "ponte" (pacote DeckLauncherFSE) abre o DeckLauncher com --xbox.
/// Nesse modo o launcher já nasce em tela cheia e escreve um pequeno registro (xbox-mode.log) para achar problemas de foco e controle.
/// </summary>
internal static class XboxMode
{
    public static bool Active { get; } = Environment.GetCommandLineArgs().Skip(1).Any(a => a.Equals("--xbox", StringComparison.OrdinalIgnoreCase));

    public static string LogPath => Path.Combine(SettingsStore.DefaultDirectory, "xbox-mode.log");

    private static readonly HashSet<string> Once = new();

    public static void Log(string text)
    {
        if (!Active) return;
        try
        {
            Directory.CreateDirectory(SettingsStore.DefaultDirectory);
            if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 200 * 1024) File.Delete(LogPath); // keep it small
            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // a log that can't be written is not worth a crash
        }
    }

    /// <summary>Writes the line only the first time this key is seen in this run.</summary>
    public static void LogOnce(string key, string text)
    {
        if (Active && Once.Add(key)) Log(text);
    }
}
