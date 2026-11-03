using System.IO;
using System.Windows;
using LiteLauncher.Core.Settings;

namespace DeckLauncher.App;

public partial class App : Application
{
    public static SettingsStore Store { get; } = new();

    public static AppSettings Settings { get; private set; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        ImportLiteLauncherSettings();
        Settings = Store.Load();

        // An unexpected error in one handler shouldn't close the whole launcher: write it down and carry on.
        DispatcherUnhandledException += (_, args) =>
        {
            CrashLog.Write(args.Exception);
            args.Handled = true;
        };

        // Hovering a control focuses it, so mouse and controller share one highlight.
        var hover = new System.Windows.Input.MouseEventHandler(OnHoverFocus);
        EventManager.RegisterClassHandler(typeof(System.Windows.Controls.Button), System.Windows.Input.Mouse.MouseEnterEvent, hover);
        EventManager.RegisterClassHandler(typeof(System.Windows.Controls.RadioButton), System.Windows.Input.Mouse.MouseEnterEvent, hover);
        EventManager.RegisterClassHandler(typeof(System.Windows.Controls.Primitives.ToggleButton), System.Windows.Input.Mouse.MouseEnterEvent, hover);

        base.OnStartup(e);
    }

    private static void OnHoverFocus(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is UIElement { IsEnabled: true, Focusable: true, IsVisible: true } element && Current.MainWindow is { IsActive: true })
            element.Focus();
    }

    /// <summary>
    /// First run only: brings over the LiteLauncher settings (Steam account, SteamGridDB key, manual games...).
    /// The keys are encrypted for this Windows user, so they keep working in the copy.
    /// </summary>
    private static void ImportLiteLauncherSettings()
    {
        try
        {
            if (File.Exists(SettingsStore.DefaultFilePath)) return;
            var old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LiteLauncher", "settings.json");
            if (!File.Exists(old)) return;
            Directory.CreateDirectory(SettingsStore.DefaultDirectory);
            File.Copy(old, SettingsStore.DefaultFilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not fatal: the launcher simply starts with empty settings.
        }
    }
}

internal static class CrashLog
{
    public static string FilePath => Path.Combine(SettingsStore.DefaultDirectory, "crash.log");

    public static void Write(Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.DefaultDirectory);
            if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 512 * 1024) File.Delete(FilePath); // keep the log small
            // Steam Web API addresses carry the key in the query string: never write it down.
            var text = System.Text.RegularExpressions.Regex.Replace(ex?.ToString() ?? "", @"(?i)(key|token|password)=[^&\s""]+", "$1=***");
            File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}\n\n");
        }
        catch (Exception io) when (io is IOException or UnauthorizedAccessException)
        {
            // Nothing more can be done if even the log can't be written.
        }
    }
}
