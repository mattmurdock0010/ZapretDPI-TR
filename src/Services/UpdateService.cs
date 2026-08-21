using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Velopack;
using Velopack.Sources;
using ZapretDPI.Views;

namespace ZapretDPI.Services;

public class UpdateService
{
    public static string CurrentVersion
    {
        get
        {
            var asm = typeof(UpdateService).Assembly;
            var infoVer = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrEmpty(infoVer))
            {
                var plusIdx = infoVer.IndexOf('+');
                var ver = plusIdx > 0 ? infoVer.Substring(0, plusIdx) : infoVer;
                return ver.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? ver : $"v{ver}";
            }
            var version = asm.GetName().Version;
            return version != null ? $"v{version.Major}.{version.Minor}.{version.Build}" : "v1.0.0";
        }
    }
    private const string RepoUrl = "https://github.com/mattmurdock0010/ZapretDPI-TR";
    private const string LatestReleaseUrl = "https://api.github.com/repos/mattmurdock0010/ZapretDPI-TR/releases/latest";

    private readonly UpdateManager? _updateManager;
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(15) };

    static UpdateService()
    {
        HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ZapretDPI-TR-Client");
    }

    public UpdateService()
    {
        try
        {
            _updateManager = new UpdateManager(new GithubSource(RepoUrl, null, false));
        }
        catch { }
    }

    public async Task CheckForUpdatesAsync(bool silent = true)
    {
        try
        {
            if (_updateManager != null && _updateManager.IsInstalled)
            {
                var newVersion = await _updateManager.CheckForUpdatesAsync();
                if (newVersion == null)
                {
                    if (!silent)
                    {
                        DarkMessageBox.Show(
                            string.Format(LocalizationService.Get("Dialog_UpToDateText"), CurrentVersion),
                            LocalizationService.Get("Dialog_UpToDateTitle"),
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                    return;
                }

                var tag = newVersion.TargetFullRelease.Version.ToString();
                var notes = await FetchReleaseNotesAsync(tag);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    var dlg = new UpdateWindow(tag, CurrentVersion, notes, _updateManager, newVersion);
                    if (Application.Current.MainWindow != null && Application.Current.MainWindow.IsLoaded)
                    {
                        dlg.Owner = Application.Current.MainWindow;
                    }
                    dlg.ShowDialog();
                });
                return;
            }

            var response = await HttpClient.GetAsync(LatestReleaseUrl);
            if (!response.IsSuccessStatusCode)
            {
                if (!silent)
                {
                    DarkMessageBox.Show(
                        LocalizationService.Get("Dialog_UpdateCheckFailed"),
                        LocalizationService.Get("Dialog_UpdateTitle"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("tag_name", out var tagProp))
            {
                var latestTag = tagProp.GetString() ?? string.Empty;
                if (IsNewerVersion(latestTag, CurrentVersion))
                {
                    var notes = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? string.Empty : string.Empty;
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        var dlg = new UpdateWindow(latestTag, CurrentVersion, notes);
                        if (Application.Current.MainWindow != null && Application.Current.MainWindow.IsLoaded)
                        {
                            dlg.Owner = Application.Current.MainWindow;
                        }
                        dlg.ShowDialog();
                    });
                }
                else
                {
                    if (!silent)
                    {
                        DarkMessageBox.Show(
                            string.Format(LocalizationService.Get("Dialog_UpToDateText"), CurrentVersion),
                            LocalizationService.Get("Dialog_UpToDateTitle"),
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (!silent)
            {
                var errTitle = LocalizationService.Get("Dialog_Error");
                DarkMessageBox.Show(
                    $"{errTitle}: {ex.Message}",
                    errTitle,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    private static async Task<string> FetchReleaseNotesAsync(string tagName)
    {
        try
        {
            var cleanTag = tagName.TrimStart('v', 'V');
            var url = $"https://api.github.com/repos/mattmurdock0010/ZapretDPI-TR/releases/tags/{cleanTag}";
            var response = await HttpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                response = await HttpClient.GetAsync(LatestReleaseUrl);
            }
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("body", out var bodyProp))
                {
                    return bodyProp.GetString() ?? string.Empty;
                }
            }
        }
        catch { }
        return string.Empty;
    }

    private static bool IsNewerVersion(string latestTag, string currentTag)
    {
        var cleanLatest = latestTag.TrimStart('v', 'V').Trim();
        var cleanCurrent = currentTag.TrimStart('v', 'V').Trim();

        if (Version.TryParse(cleanLatest, out var vLatest) &&
            Version.TryParse(cleanCurrent, out var vCurrent))
        {
            return vLatest > vCurrent;
        }

        return !string.Equals(cleanLatest, cleanCurrent, StringComparison.OrdinalIgnoreCase);
    }
}
