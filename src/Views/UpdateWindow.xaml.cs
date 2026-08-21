using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Velopack;
using ZapretDPI.Services;

namespace ZapretDPI.Views;

public partial class UpdateWindow : Window
{
    private readonly UpdateManager? _updateManager;
    private readonly UpdateInfo? _velopackUpdateInfo;
    private readonly string _newVersionTag;
    private readonly string _currentVersionTag;
    private readonly string _releaseNotes;
    private readonly bool _isVelopackInstalled;
    private bool _isUpdating;

    public UpdateWindow(
        string newVersionTag,
        string currentVersionTag,
        string releaseNotes,
        UpdateManager? updateManager = null,
        UpdateInfo? velopackUpdateInfo = null)
    {
        InitializeComponent();

        _newVersionTag = newVersionTag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? newVersionTag : $"v{newVersionTag}";
        _currentVersionTag = currentVersionTag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? currentVersionTag : $"v{currentVersionTag}";
        _releaseNotes = string.IsNullOrWhiteSpace(releaseNotes) ? "Detaylı sürüm notu bulunamadı." : releaseNotes;
        _updateManager = updateManager;
        _velopackUpdateInfo = velopackUpdateInfo;
        _isVelopackInstalled = _updateManager != null && _updateManager.IsInstalled && _velopackUpdateInfo != null;

        ApplyLocalization();
    }

    private void ApplyLocalization()
    {
        TxtTitleBar.Text = LocalizationService.Get("Dialog_UpdateAvailableTitle");
        TxtHeaderTitle.Text = LocalizationService.Get("Dialog_UpdateAvailableTitle");
        TxtCurrentVer.Text = $"Mevcut: {_currentVersionTag}";
        TxtNewVer.Text = $"Yeni: {_newVersionTag}";
        TxtNotesHeader.Text = LocalizationService.Get("Dialog_ReleaseNotesTitle");
        TxtReleaseNotes.Markdown = _releaseNotes;
        BtnLater.Content = LocalizationService.Get("Dialog_BtnLater");
        TxtBtnUpdate.Text = LocalizationService.Get("Dialog_BtnUpdateNow");

        if (!_isVelopackInstalled)
        {
            TxtBtnUpdate.Text = "GitHub İndirme Sayfası";
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        if (!_isUpdating)
        {
            Close();
        }
    }

    private void BtnLater_Click(object sender, RoutedEventArgs e)
    {
        if (!_isUpdating)
        {
            Close();
        }
    }

    private async void BtnUpdateNow_Click(object sender, RoutedEventArgs e)
    {
        if (_isUpdating) return;

        if (!_isVelopackInstalled)
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://github.com/mattmurdock0010/ZapretDPI-TR/releases/latest") { UseShellExecute = true });
            }
            catch { }
            Close();
            return;
        }

        _isUpdating = true;
        BtnLater.IsEnabled = false;
        BtnUpdateNow.IsEnabled = false;
        PnlProgress.Visibility = Visibility.Visible;
        TxtProgressStatus.Text = LocalizationService.Get("Dialog_DownloadingUpdate");

        try
        {
            int lastProgress = -1;
            await Task.Run(async () =>
            {
                await _updateManager!.DownloadUpdatesAsync(_velopackUpdateInfo!, progress =>
                {
                    if (progress != lastProgress)
                    {
                        lastProgress = progress;
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            PbDownload.Value = progress;
                            TxtProgressPercent.Text = $"%{progress}";
                        }));
                    }
                });
            });

            TxtProgressStatus.Text = LocalizationService.Get("Dialog_ApplyingUpdate");
            await Task.Delay(500);

            SystemCleanupService.StopAllServicesAndProcessesSilently();
            _updateManager!.ApplyUpdatesAndRestart(_velopackUpdateInfo!);
        }
        catch (Exception ex)
        {
            PnlProgress.Visibility = Visibility.Collapsed;
            _isUpdating = false;
            BtnLater.IsEnabled = true;
            BtnUpdateNow.IsEnabled = true;

            var errTitle = LocalizationService.Get("Dialog_Error");
            DarkMessageBox.Show($"{errTitle}: {ex.Message}", errTitle, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
