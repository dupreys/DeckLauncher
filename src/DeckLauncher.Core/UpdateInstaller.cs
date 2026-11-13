using System.Net.Http;
using System.Security.Cryptography;

namespace DeckLauncher.Core;

/// <summary>
/// Downloads the installer of a newer release so the launcher can update itself. The file is only kept when it is the size and the SHA-256
/// GitHub lists for that release asset; a release that lists neither is not installed automatically.
/// </summary>
public static class UpdateInstaller
{
    /// <summary>True when the release carries an installer that can be checked after the download.</summary>
    public static bool CanInstall(UpdateInfo info) => info.InstallerUrl is not null && (info.Sha256 is not null || info.InstallerSize > 0);

    public static string FileFor(string folder, UpdateInfo info) =>
        Path.Combine(folder, Path.GetFileName(new Uri(info.InstallerUrl ?? throw new InvalidOperationException("no installer")).AbsolutePath));

    /// <summary>The file exists, is a program (starts with "MZ") and matches the size and the SHA-256 of the release.</summary>
    public static async Task<bool> VerifyAsync(string file, UpdateInfo info, CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(file)) return false;
            var length = new FileInfo(file).Length;
            if (info.InstallerSize > 0 && length != info.InstallerSize) return false;
            await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            var head = new byte[2];
            if (await stream.ReadAsync(head, ct) != 2 || head[0] != (byte)'M' || head[1] != (byte)'Z') return false;
            if (info.Sha256 is null) return info.InstallerSize > 0;
            stream.Position = 0;
            return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).Equals(info.Sha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Downloads (or reuses an earlier, verified download) and returns the installer's path. Throws when the file does not match the release.</summary>
    public static async Task<string> DownloadAsync(HttpClient http, UpdateInfo info, string folder, IProgress<double>? progress, CancellationToken ct = default)
    {
        if (!CanInstall(info)) throw new InvalidOperationException("This release has no installer that can be checked.");
        Directory.CreateDirectory(folder);
        var file = FileFor(folder, info);
        if (await VerifyAsync(file, info, ct)) { progress?.Report(1); return file; }

        var part = file + ".part";
        try
        {
            using var response = await http.GetAsync(info.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? info.InstallerSize;
            await using (var source = await response.Content.ReadAsStreamAsync(ct))
            await using (var target = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long done = 0;
                var lastReport = Environment.TickCount64;
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), ct);
                    done += read;
                    if (total > 0 && Environment.TickCount64 - lastReport > 250) { lastReport = Environment.TickCount64; progress?.Report(Math.Min(1, (double)done / total)); }
                }
            }
            File.Move(part, file, overwrite: true);
            if (!await VerifyAsync(file, info, ct))
            {
                File.Delete(file);
                throw new InvalidDataException("The downloaded installer does not match the release.");
            }
            progress?.Report(1);
            return file;
        }
        finally
        {
            try { if (File.Exists(part)) File.Delete(part); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* removed with the update folder later */ }
        }
    }

    /// <summary>Removes downloaded installers (after the update was installed or when there is nothing newer).</summary>
    public static void Clean(string folder)
    {
        try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* in use: next time */ }
    }
}
