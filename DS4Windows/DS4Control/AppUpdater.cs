using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DS4Windows;
using DS4WinWPF.ApiDTO;
using DS4WinWPF.Translations;
using HttpProgress;

namespace DS4WinWPF.DS4Control;

/// <summary>
/// Updates a copy installed by the Inno Setup installer: downloads the setup of
/// the new release, checks it against the release's SHA256SUMS.txt and runs it
/// silently. The installer closes DS4Windows, updates the files in place (same
/// folder, same per-user or all-users mode) and starts DS4Windows again.
/// Portable copies (zip, bin folder) are updated by hand from the release page.
/// </summary>
public static class AppUpdater
{
    public const string ChecksumsAssetName = "SHA256SUMS.txt";

    // /SILENT shows only a progress window; previous folder, mode, language and
    // tasks are reused by the installer. /UPDATE=1 makes it start DS4Windows
    // again when done (see IsAppUpdate in installer\DS4Windows.iss)
    private const string InstallerArguments = "/SILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /NORESTART /UPDATE=1";

    public static bool IsInstalledCopy =>
        File.Exists(Path.Combine(Global.exedirpath, App.InstalledMarkerFileName));

    // A self-contained build ships the runtime next to the exe and must be
    // updated with the self-contained installer
    public static bool IsSelfContained =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, "coreclr.dll"));

    public static string DownloadDirectory =>
        Path.Combine(Path.GetTempPath(), "DS4Windows_update");

    // Same names as installer\build.ps1
    public static string InstallerAssetName(string version, bool selfContained) =>
        $"DS4Windows_{version}_x64{(selfContained ? "_selfcontained" : "")}_Setup.exe";

    /// <summary>
    /// Finds the checksum of a file in sha256sum output ("&lt;hash&gt;  &lt;name&gt;",
    /// or "&lt;hash&gt; *&lt;name&gt;" in binary mode). Returns the lowercase hash, or
    /// null if the file is not listed.
    /// </summary>
    public static string FindChecksum(string sums, string fileName)
    {
        if (string.IsNullOrEmpty(sums)) return null;

        foreach (string rawLine in sums.Split('\n'))
        {
            string line = rawLine.Trim();
            int sep = line.IndexOf(' ');
            if (sep <= 0) continue;

            string hash = line[..sep];
            string name = line[(sep + 1)..].TrimStart(' ', '*');
            if (hash.Length == 64 && hash.All(Uri.IsHexDigit) &&
                string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase))
            {
                return hash.ToLowerInvariant();
            }
        }

        return null;
    }

    public static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// <summary>
    /// Downloads and checks the installer of the given release. Returns the path
    /// of the verified installer. Throws <see cref="AppUpdateException"/> with a
    /// user-facing reason, or the underlying network exception.
    /// </summary>
    public static async Task<string> DownloadInstallerAsync(string version,
        IProgress<double> progress, CancellationToken cancelToken)
    {
        string installerName = InstallerAssetName(version, IsSelfContained);

        GithubRelease release = await App.requestClient.GetFromJsonAsync<GithubRelease>(
            $"{Changelog.GITHUB_RELEASES_API_URI}/tags/v{version}", cancelToken);
        GithubReleaseAsset installer = FindAsset(release, installerName);
        GithubReleaseAsset sumsAsset = FindAsset(release, ChecksumsAssetName);
        if (installer is null)
            throw new AppUpdateException(string.Format(Strings.UpdaterWin_NoInstallerAsset, installerName));
        if (sumsAsset is null)
            throw new AppUpdateException(string.Format(Strings.UpdaterWin_NoChecksum, installerName));

        string sums = await App.requestClient.GetStringAsync(sumsAsset.BrowserDownloadUrl, cancelToken);
        string expectedHash = FindChecksum(sums, installerName);
        if (expectedHash is null)
            throw new AppUpdateException(string.Format(Strings.UpdaterWin_NoChecksum, installerName));

        string dir = DownloadDirectory;
        CleanDownloadDirectory(dir);
        Directory.CreateDirectory(dir);
        string finalPath = Path.Combine(dir, installerName);
        string partPath = finalPath + ".part";

        Progress<ICopyProgress> copyProgress = new(x => progress?.Report(x.PercentComplete));
        try
        {
            using (FileStream stream = new(partPath, FileMode.Create, FileAccess.Write))
            {
                using HttpResponseMessage response = await App.requestClient.GetAsync(
                    installer.BrowserDownloadUrl, stream, copyProgress, cancelToken);
                response.EnsureSuccessStatusCode();
            }

            if (ComputeSha256(partPath) != expectedHash)
                throw new AppUpdateException(Strings.UpdaterWin_ChecksumMismatch);

            File.Move(partPath, finalPath, true);
        }
        finally
        {
            TryDelete(partPath);
        }

        return finalPath;
    }

    /// <summary>
    /// Starts the downloaded installer. The installer asks for elevation itself
    /// when the existing copy was installed for all users.
    /// </summary>
    public static bool LaunchInstaller(string installerPath)
    {
        try
        {
            string logPath = Path.Combine(Path.GetDirectoryName(installerPath), "install.log");
            ProcessStartInfo startInfo = new(installerPath)
            {
                Arguments = $"{InstallerArguments} /LOG=\"{logPath}\"",
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(installerPath),
            };
            using Process process = Process.Start(startInfo);
            return process is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static GithubReleaseAsset FindAsset(GithubRelease release, string name) =>
        release?.Assets?.FirstOrDefault(a =>
            string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase) &&
            Uri.TryCreate(a.BrowserDownloadUrl, UriKind.Absolute, out Uri uri) &&
            uri.Scheme == Uri.UriSchemeHttps);

    // Installers left by previous updates; one still running stays in place
    private static void CleanDownloadDirectory(string dir)
    {
        if (!Directory.Exists(dir)) return;

        foreach (string file in Directory.GetFiles(dir))
            TryDelete(file);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception) { }
    }
}

public class AppUpdateException : Exception
{
    public AppUpdateException(string message) : base(message)
    {
    }
}
