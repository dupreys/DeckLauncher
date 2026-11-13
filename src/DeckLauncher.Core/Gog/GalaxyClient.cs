using Microsoft.Win32;

namespace DeckLauncher.Core.Gog;

/// <summary>Finds the GOG Galaxy program (the GOG launcher), when it is installed.</summary>
public static class GalaxyClient
{
    public static string? FindExe()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = baseKey.OpenSubKey(@"SOFTWARE\GOG.com\GalaxyClient\paths");
                if (key?.GetValue("client") is string folder && !string.IsNullOrWhiteSpace(folder))
                {
                    var exe = Path.Combine(folder, "GalaxyClient.exe");
                    if (File.Exists(exe)) return exe;
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // fall through to the default location
        }
        var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "GOG Galaxy", "GalaxyClient.exe");
        return File.Exists(fallback) ? fallback : null;
    }

    public static bool IsInstalled => FindExe() is not null;
}
