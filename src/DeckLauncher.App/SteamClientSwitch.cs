using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using DeckLauncher.Core.Steam;

namespace DeckLauncher.App;

/// <summary>
/// Makes the official Steam client start on a given account. Nothing is typed or sent to Steam: the launcher only sets
/// the client's own "auto login" value (the account must have "remember password" on) and restarts the client, which signs in by itself.
/// </summary>
internal static class SteamClientSwitch
{
    private const string SteamKey = @"Software\Valve\Steam";

    public enum Result { AlreadyActive, Switched, NeedsPassword, GameRunning, Failed }

    /// <summary>SteamID32 of the account the running client is signed in to; 0 when none.</summary>
    public static long ActiveUser()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SteamKey + @"\ActiveProcess");
            return key?.GetValue("ActiveUser") is int user ? user : 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return 0;
        }
    }

    public static bool IsSteamRunning() => Process.GetProcessesByName("steam").Length > 0;

    /// <summary>Switches the client to the account. Closes Steam politely and starts it again when it was running.</summary>
    public static async Task<Result> SwitchAsync(string steamPath, SteamLocalAccount account, bool gameRunning)
    {
        if (long.TryParse(SteamAccountFilter.AccountId32(account.SteamId64), out var id32) && ActiveUser() == id32) return Result.AlreadyActive;
        if (!account.RememberPassword) return Result.NeedsPassword;
        if (gameRunning) return Result.GameRunning;

        try
        {
            var wasRunning = IsSteamRunning();
            var exe = Path.Combine(steamPath, "steam.exe");
            if (wasRunning && File.Exists(exe))
            {
                Process.Start(new ProcessStartInfo(exe, "-shutdown") { UseShellExecute = false, CreateNoWindow = true });
                for (var i = 0; i < 40 && IsSteamRunning(); i++) await Task.Delay(500); // up to 20 s for Steam to close by itself
                if (IsSteamRunning()) return Result.Failed;
            }

            using (var key = Registry.CurrentUser.CreateSubKey(SteamKey))
            {
                key.SetValue("AutoLoginUser", account.AccountName, RegistryValueKind.String);
                key.SetValue("RememberPassword", 1, RegistryValueKind.DWord);
            }

            if (wasRunning && File.Exists(exe)) Process.Start(new ProcessStartInfo(exe, "-silent") { UseShellExecute = false });
            return Result.Switched;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException)
        {
            return Result.Failed;
        }
    }
}
