using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using ZapretDPI.Models;
using ZapretDPI.Services;
using ZapretDPI.ViewModels;
using ZapretDPI.Views;

namespace ZapretDPI;

public partial class MainWindow : Window
{
    private readonly ConfigService _config;
    private readonly DnsManager _dnsManager;
    private readonly ZapretProcessManager _processManager;
    private readonly WindowsServiceManager _serviceManager;
    private readonly DnscryptManager _dnscryptManager;
    private readonly BlockcheckRunner _blockcheckRunner;
    private readonly LanShareManager _lanShareManager;
    private readonly UpdateService _updateService;

    private AppConfig _appConfig;
    private bool _isInitializing = true;
    private CancellationTokenSource? _analysisCts;
    private DispatcherTimer? _consoleTimer;
    private long _lastLogSize = 0;

    private readonly MainViewModel _viewModel;

    public MainWindow(
        ConfigService configService,
        DnsManager dnsManager,
        ZapretProcessManager processManager,
        WindowsServiceManager serviceManager,
        DnscryptManager dnscryptManager,
        BlockcheckRunner blockcheckRunner,
        LanShareManager lanShareManager,
        UpdateService updateService,
        MainViewModel viewModel)
    {
        _config = configService;
        _dnsManager = dnsManager;
        _processManager = processManager;
        _serviceManager = serviceManager;
        _dnscryptManager = dnscryptManager;
        _blockcheckRunner = blockcheckRunner;
        _lanShareManager = lanShareManager;
        _updateService = updateService;
        _viewModel = viewModel;

        DataContext = _viewModel;
        _appConfig = _config.LoadConfig();

        InitializeComponent();

        _processManager.ProcessCrashed += ProcessManager_ProcessCrashed;
        _processManager.StateChanged += async (s, e) => await Dispatcher.InvokeAsync(RefreshDashboardAsync);
        _serviceManager.StateChanged += async (s, e) => await Dispatcher.InvokeAsync(RefreshDashboardAsync);
        _dnscryptManager.StateChanged += async (s, e) => await Dispatcher.InvokeAsync(RefreshDashboardAsync);

        _dnscryptManager.FallbackOccurred += (s, msg) => Dispatcher.InvokeAsync(() =>
        {
            DarkMessageBox.Show(msg, LocalizationService.Get("Dialog_Warning") ?? "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
        });

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;

        // Listen for sleep/wake power events to recover services
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Disable action buttons initially until the first RefreshDashboardAsync completes
        BtnQuickStart.IsEnabled = false;
        BtnQuickService.IsEnabled = false;
        BtnQuickDnscrypt.IsEnabled = false;
        BtnToggleLanShare.IsEnabled = false;

        CmbFilterMode.SelectedIndex = (int)_appConfig.FilterMode;

        LoadFilesToEditors();

        PopulateStrategyPresets();

        CmbStrategyPreset.SelectionChanged += CmbStrategyPreset_SelectionChanged;

        var lang = string.IsNullOrWhiteSpace(_appConfig.Language) ? "tr" : _appConfig.Language.ToLowerInvariant();
        LocalizationService.SetLanguage(lang);
        for (int i = 0; i < CmbLanguage.Items.Count; i++)
        {
            if (CmbLanguage.Items[i] is ComboBoxItem item && (string)item.Tag == lang)
            {
                CmbLanguage.SelectedIndex = i;
                break;
            }
        }
        ApplyLocalization();

        await RefreshDashboardAsync();

        await InitialSystemCheckAsync();

        _isInitializing = false;
        await RefreshDashboardAsync();

        _ = _updateService.CheckForUpdatesAsync(silent: true);
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _config.SaveConfig(_appConfig);
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
    }

    /// <summary>
    /// Handles system sleep/wake transitions.
    /// </summary>
    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            Serilog.Log.Information("[PowerMode] System resumed from sleep. Triggering service recovery...");
            _ = RecoverServicesAsync();
        }
    }

    /// <summary>
    /// Handles network availability changes (e.g. Modern Standby Wi-Fi drop/reconnect, cable unplug/replug).
    /// </summary>
    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        if (e.IsAvailable)
        {
            Serilog.Log.Information("[NetworkChange] Network became available. Triggering service recovery...");
            _ = RecoverServicesAsync();
        }
    }

    private bool _isRecovering = false;

    /// <summary>
    /// Recovers DNSCrypt and WinDivert services. Used by both Power and Network events.
    /// </summary>
    private async Task RecoverServicesAsync()
    {
        if (_isRecovering) return;
        _isRecovering = true;

        try
        {
            // Wait for network adapter to truly settle (up to 15 seconds)
            bool networkReady = false;
            for (int i = 0; i < 15; i++)
            {
                await Task.Delay(1000);
                if (NetworkInterface.GetIsNetworkAvailable())
                {
                    networkReady = true;
                    break;
                }
            }

            if (!networkReady)
            {
                Serilog.Log.Warning("[Recovery] Network did not settle after 15s. Attempting recovery anyway.");
            }

            // Extra settle time for adapter/driver initialization
            await Task.Delay(2000);

            // 1) Kill any zombie processes that might be stuck after Modern Standby
            Serilog.Log.Information("[Recovery] Killing potentially stuck background processes...");
            foreach (var pName in new[] { "winws2", "dnscrypt-proxy" })
            {
                foreach (var proc in Process.GetProcessesByName(pName))
                {
                    try { proc.Kill(true); proc.WaitForExit(1000); } catch { }
                }
            }

            // 2) Recover DNSCrypt: if DNS is 127.0.0.1, it needs dnscrypt-proxy to work.
            try
            {
                await _dnscryptManager.CheckAndRecoverDnsAsync();
                Serilog.Log.Information("[Recovery] DNSCrypt recovery completed.");
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "[Recovery] DNSCrypt recovery failed.");
            }

            // 3) Recover WinDivert/Zapret service: re-kick it unconditionally if it is installed
            try
            {
                if (await _serviceManager.IsServiceInstalledAsync())
                {
                    Serilog.Log.Information("[Recovery] Zapret service is installed. Force restarting...");
                    var psiStop = new ProcessStartInfo
                    {
                        FileName = "sc.exe",
                        Arguments = "stop ZapretService",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var pStop = Process.Start(psiStop);
                    pStop?.WaitForExit(2000);

                    var psiStart = new ProcessStartInfo
                    {
                        FileName = "sc.exe",
                        Arguments = "start ZapretService",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var pStart = Process.Start(psiStart);
                    pStart?.WaitForExit(2000);

                    // Wait up to 5 seconds for it to start
                    for (int i = 0; i < 10; i++)
                    {
                        await Task.Delay(500);
                        if (await _serviceManager.IsServiceRunningAsync()) break;
                    }
                    Serilog.Log.Information("[Recovery] Zapret service restart completed.");
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "[Recovery] Zapret service recovery failed.");
            }

            // 3) Refresh UI
            await Dispatcher.InvokeAsync(RefreshDashboardAsync);
        }
        finally
        {
            _isRecovering = false;
        }
    }


    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        await _updateService.CheckForUpdatesAsync(silent: false);
    }

    private void CmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbLanguage.SelectedItem is ComboBoxItem item && item.Tag is string lang)
        {
            _appConfig.Language = lang;
            LocalizationService.SetLanguage(lang);
            ApplyLocalization();
            _config.SaveConfig(_appConfig);
        }
    }

#pragma warning disable CS8602
    private void ApplyLocalization()
    {
        NavOverview.Content = LocalizationService.Get("Nav_Overview");
        NavRules.Content = LocalizationService.Get("Nav_Rules");
        NavFilters.Content = LocalizationService.Get("Nav_Filters");
        NavDns.Content = LocalizationService.Get("Nav_Dns");
        NavLan.Content = LocalizationService.Get("Nav_Lan");
        NavAnalysis.Content = LocalizationService.Get("Nav_Analysis");
        NavConsole.Content = LocalizationService.Get("Nav_Console");
        NavRecovery.Content = LocalizationService.Get("Nav_Recovery");
        NavAbout.Content = LocalizationService.Get("Nav_About");
        if (LblFooterVersionHeader != null) LblFooterVersionHeader.Text = LocalizationService.Get("Footer_Version");
        if (TxtFooterVersionValue != null) TxtFooterVersionValue.Text = UpdateService.CurrentVersion;

        LblOverviewTitle.Text = LocalizationService.Get("Overview_Title");
        LblPillRouting.Text = LocalizationService.Get("Pill_Routing");
        LblPillEngine.Text = LocalizationService.Get("Pill_Engine");
        LblPillService.Text = LocalizationService.Get("Pill_Service");
        LblPillDns.Text = LocalizationService.Get("Pill_Dns");
        LblPillWinDivert.Text = LocalizationService.Get("Pill_WinDivert");
        LblPillLan.Text = LocalizationService.Get("Pill_Lan");

        LblActionSectionTitle.Text = LocalizationService.Get("Action_SectionTitle");
        LblActionSectionDesc.Text = LocalizationService.Get("Action_SectionDesc");

        if (LblOverviewActiveProfileTitle != null) LblOverviewActiveProfileTitle.Text = LocalizationService.Get("Overview_ActiveProfileTitle");
        if (LblOverviewActiveProfileDesc != null) LblOverviewActiveProfileDesc.Text = LocalizationService.Get("Overview_ActiveProfileDesc");

        LblQuickStartTitle.Text = LocalizationService.Get("Action_QuickStartTitle");
        LblQuickStartDesc.Text = LocalizationService.Get("Action_QuickStartDesc");

        LblQuickServiceTitle.Text = LocalizationService.Get("Action_ServiceTitle");
        LblQuickServiceDesc.Text = LocalizationService.Get("Action_ServiceDesc");

        LblQuickDnscryptTitle.Text = LocalizationService.Get("Action_DnscryptTitle");
        LblQuickDnscryptDesc.Text = LocalizationService.Get("Action_DnscryptDesc");

        LblRulesTitle.Text = LocalizationService.Get("Rules_Title");
        LblRulesDesc.Text = LocalizationService.Get("Rules_Desc");
        LblFilterMode.Text = LocalizationService.Get("Engine_FilterMode");
        if (CmbFilterAuto != null) CmbFilterAuto.Content = LocalizationService.Get("Filter_Auto");
        if (CmbFilterManual != null) CmbFilterManual.Content = LocalizationService.Get("Filter_Manual");
        if (CmbFilterOff != null) CmbFilterOff.Content = LocalizationService.Get("Filter_Off");
        LblPresetTitle.Text = LocalizationService.Get("Rules_PresetTitle");
        LblActiveParams.Text = LocalizationService.Get("Rules_ActiveParams");
        if (BtnSaveStrategy != null) BtnSaveStrategy.Content = LocalizationService.Get("Btn_SaveStrategy");
        if (BtnDeleteStrategy != null) BtnDeleteStrategy.Content = LocalizationService.Get("Msg_ProfileDeleteTitle");
        if (BtnRestoreDefaults != null) BtnRestoreDefaults.Content = LocalizationService.Get("Btn_RestoreDefaults");

        LblFiltersTitle.Text = LocalizationService.Get("Filters_Title");
        LblFiltersDesc.Text = LocalizationService.Get("Filters_Desc");
        LblHostlistTitle.Text = LocalizationService.Get("Filters_Hostlist");
        LblExcludelistTitle.Text = LocalizationService.Get("Filters_Excludelist");
        BtnSaveHostlist.Content = LocalizationService.Get("Btn_SaveHostlist");
        BtnSaveExcludelist.Content = LocalizationService.Get("Btn_SaveExcludelist");

        LblDnsTitle.Text = LocalizationService.Get("Dns_Title");
        LblDnsDesc.Text = LocalizationService.Get("Dns_Desc");
        LblPoisonCheckTitle.Text = LocalizationService.Get("Dns_PoisonCheckTitle");
        LblPoisonCheckDesc.Text = LocalizationService.Get("Dns_PoisonCheckDesc");
        if (TxtTestDnsPoison != null) TxtTestDnsPoison.Text = LocalizationService.Get("Btn_TestDns");
        LblQuickSwitcherTitle.Text = LocalizationService.Get("Dns_QuickSwitcher");
        BtnSetGoogleDns.Content = LocalizationService.Get("Btn_GoogleDns");
        BtnSetAdguardDns.Content = LocalizationService.Get("Btn_AdguardDns");
        BtnSetCloudflareDns.Content = LocalizationService.Get("Btn_CloudflareDns");
        BtnResetDnsDhcp.Content = LocalizationService.Get("Btn_ResetDhcp");

        LblLanTitle.Text = LocalizationService.Get("Lan_Title");
        LblLanDesc.Text = LocalizationService.Get("Lan_Desc");
        LblRouterTitle.Text = LocalizationService.Get("Lan_RouterTitle");
        LblRouterDesc.Text = LocalizationService.Get("Lan_RouterDesc");

        LblAnalysisTitle.Text = LocalizationService.Get("Analysis_Title");
        LblAnalysisDesc.Text = LocalizationService.Get("Analysis_Desc");
        LblAnalysisCardTitle.Text = LocalizationService.Get("Analysis_CardTitle");
        LblAnalysisCardDesc.Text = LocalizationService.Get("Analysis_CardDesc");
        if (TxtStartAnalysis != null) TxtStartAnalysis.Text = _analysisCts != null ? LocalizationService.Get("Btn_StopAnalysis") : LocalizationService.Get("Btn_StartAnalysis");
        if (EmojiStartAnalysis != null) EmojiStartAnalysis.Text = _analysisCts != null ? "⏹️" : "▶️";
        LblScanMode.Text = LocalizationService.Get("Analysis_ScanMode");
        RbScanFast.Content = LocalizationService.Get("Analysis_ScanFast");
        RbScanSmart.Content = LocalizationService.Get("Analysis_ScanSmart");
        RbScanDeep.Content = LocalizationService.Get("Analysis_ScanDeep");

        LblRecoveryTitle.Text = LocalizationService.Get("Recovery_Title");
        LblRecoveryDesc.Text = LocalizationService.Get("Recovery_Desc");
        LblResetTitle.Text = LocalizationService.Get("Recovery_ResetTitle");
        if (LblRecoveryListHeader != null) LblRecoveryListHeader.Text = LocalizationService.Get("Recovery_ListHeader");
        if (LblRecoveryItem1 != null) LblRecoveryItem1.Text = LocalizationService.Get("Recovery_Item1");
        if (LblRecoveryItem2 != null) LblRecoveryItem2.Text = LocalizationService.Get("Recovery_Item2");
        if (LblRecoveryItem3 != null) LblRecoveryItem3.Text = LocalizationService.Get("Recovery_Item3");
        if (LblRecoveryItem4 != null) LblRecoveryItem4.Text = LocalizationService.Get("Recovery_Item4");
        if (TxtFullCleanup != null) TxtFullCleanup.Text = LocalizationService.Get("Btn_FullCleanup");

        if (LblAboutTitle != null) LblAboutTitle.Text = LocalizationService.Get("About_Title");
        if (LblAboutSubtitle != null) LblAboutSubtitle.Text = LocalizationService.Get("About_Subtitle");
        if (LblAboutDescription != null) LblAboutDescription.Text = LocalizationService.Get("About_Description");
        if (LblAboutPlatformCardTitle != null) LblAboutPlatformCardTitle.Text = LocalizationService.Get("About_PlatformCardTitle");
        if (LblAboutPlatformTitle != null) LblAboutPlatformTitle.Text = LocalizationService.Get("About_Platform");
        if (LblAboutArchTitle != null) LblAboutArchTitle.Text = LocalizationService.Get("About_Arch");
        if (LblAboutRuntimeTitle != null) LblAboutRuntimeTitle.Text = LocalizationService.Get("About_Runtime");
        if (LblAboutLanguageTitle != null) LblAboutLanguageTitle.Text = LocalizationService.Get("About_Language");
        if (LblAboutDpiCardTitle != null) LblAboutDpiCardTitle.Text = LocalizationService.Get("About_DpiCardTitle");
        if (LblAboutCoreTitle != null) LblAboutCoreTitle.Text = LocalizationService.Get("About_Core");
        if (LblAboutDriverTitle != null) LblAboutDriverTitle.Text = LocalizationService.Get("About_Driver");
        if (LblAboutDnsTitle != null) LblAboutDnsTitle.Text = LocalizationService.Get("About_Dns");
        if (LblAboutLanTitle != null) LblAboutLanTitle.Text = LocalizationService.Get("About_Lan");
        if (LblAboutLicenseHeader != null) LblAboutLicenseHeader.Text = LocalizationService.Get("About_LicenseTitle");
        if (LblAboutLicense != null) LblAboutLicense.Text = LocalizationService.Get("About_License");
        if (TxtOpenRepo != null) TxtOpenRepo.Text = LocalizationService.Get("About_BtnRepo");
        if (TxtCheckUpdatesAbout != null) TxtCheckUpdatesAbout.Text = LocalizationService.Get("Btn_CheckUpdates");

        if (LblDnscryptServerTitle != null) LblDnscryptServerTitle.Text = LocalizationService.Get("Dns_DnscryptTitle");
        if (LblDnscryptServerDesc != null) LblDnscryptServerDesc.Text = LocalizationService.Get("Dns_DnscryptDesc");

        if (LblConsoleTitle != null) LblConsoleTitle.Text = LocalizationService.Get("Console_Title");
        if (LblConsoleDesc != null) LblConsoleDesc.Text = LocalizationService.Get("Console_Desc");
        if (BtnClearLogs != null) BtnClearLogs.Content = LocalizationService.Get("Console_BtnClear");
        if (BtnOpenLogDir != null) BtnOpenLogDir.Content = LocalizationService.Get("Console_BtnOpenDir");

        PopulateAboutInfo();

        PopulateStrategyPresets();
        _ = RefreshDashboardAsync();
    }
#pragma warning restore CS8602

    private void NavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || rb.Tag is not string tag) return;

        if (PageOverview is null || PageRules is null ||
            PageFilters is null || PageDns is null || PageLan is null ||
            PageAnalysis is null || PageRecovery is null || PageAbout is null || PageConsole is null) return;

        PageOverview.Visibility = tag == "Overview" ? Visibility.Visible : Visibility.Collapsed;
        PageRules.Visibility = tag == "Rules" ? Visibility.Visible : Visibility.Collapsed;
        PageFilters.Visibility = tag == "Filters" ? Visibility.Visible : Visibility.Collapsed;
        PageDns.Visibility = tag == "Dns" ? Visibility.Visible : Visibility.Collapsed;
        PageLan.Visibility = tag == "Lan" ? Visibility.Visible : Visibility.Collapsed;
        PageAnalysis.Visibility = tag == "Analysis" ? Visibility.Visible : Visibility.Collapsed;
        PageConsole.Visibility = tag == "Console" ? Visibility.Visible : Visibility.Collapsed;
        PageRecovery.Visibility = tag == "Recovery" ? Visibility.Visible : Visibility.Collapsed;
        PageAbout.Visibility = tag == "About" ? Visibility.Visible : Visibility.Collapsed;

        var activePage = tag switch
        {
            "Overview" => PageOverview,
            "Rules" => PageRules,
            "Filters" => PageFilters,
            "Dns" => PageDns,
            "Lan" => PageLan,
            "Analysis" => PageAnalysis,
            "Console" => PageConsole,
            "Recovery" => PageRecovery,
            "About" => PageAbout,
            _ => null
        };

        if (activePage != null)
        {
            var fadeIn = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = new Duration(TimeSpan.FromMilliseconds(300))
            };
            activePage.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }

        if (tag == "About")
        {
            PopulateAboutInfo();
        }

        if (tag == "Console")
        {
            StartConsoleTimer();
        }
        else
        {
            StopConsoleTimer();
        }
    }

    private void StartConsoleTimer()
    {
        if (_consoleTimer != null) return;
        _consoleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _consoleTimer.Tick += (s, e) => UpdateConsoleLog();
        _consoleTimer.Start();
        UpdateConsoleLog(); // initial load
    }

    private void StopConsoleTimer()
    {
        _consoleTimer?.Stop();
        _consoleTimer = null;
    }

    private void UpdateConsoleLog()
    {
        if (TxtLiveConsole == null) return;

        string logPath1 = @"C:\ProgramData\ZapretDPI-TR\zapret_crash.log";
        string logPath2 = Path.Combine(_config.ZapretWinwsDir, "zapret-service.out.log");
        string logPath3 = Path.Combine(_config.ZapretWinwsDir, "zapret-service.err.log");

        var latestFile = logPath1;
        var lastWrite = DateTime.MinValue;

        foreach (var path in new[] { logPath1, logPath2, logPath3 })
        {
            if (File.Exists(path))
            {
                var lw = File.GetLastWriteTimeUtc(path);
                if (lw > lastWrite)
                {
                    lastWrite = lw;
                    latestFile = path;
                }
            }
        }

        if (File.Exists(latestFile))
        {
            try
            {
                var fileInfo = new FileInfo(latestFile);
                if (fileInfo.Length == _lastLogSize) return;

                using var fs = new FileStream(latestFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

                // If file is huge, only read the last 50KB to avoid freezing UI
                long offset = Math.Max(0, fs.Length - 50000);
                if (offset > 0) fs.Seek(offset, SeekOrigin.Begin);

                using var sr = new StreamReader(fs);
                var content = sr.ReadToEnd();

                if (offset > 0)
                    TxtLiveConsole.Text = "[...]\r\n" + content;
                else
                    TxtLiveConsole.Text = content;

                _lastLogSize = fs.Length;
                TxtLiveConsole.ScrollToEnd();
            }
            catch { }
        }
    }

    private void BtnClearLogs_Click(object sender, RoutedEventArgs e)
    {
        if (TxtLiveConsole != null) TxtLiveConsole.Text = "";
        _lastLogSize = 0;
        foreach (var path in new[] {
            @"C:\ProgramData\ZapretDPI-TR\zapret_crash.log",
            Path.Combine(_config.ZapretWinwsDir, "zapret-service.out.log"),
            Path.Combine(_config.ZapretWinwsDir, "zapret-service.err.log")
        })
        {
            try { if (File.Exists(path)) File.WriteAllText(path, ""); } catch { }
        }
        UpdateConsoleLog();
    }

    private void BtnOpenLogDir_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string dir = @"C:\ProgramData\ZapretDPI-TR";
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            Process.Start("explorer.exe", dir);
        }
        catch { }
    }


    private async Task InitialSystemCheckAsync()
    {
        TxtDashboardSubtitle.Text = LocalizationService.Get("Overview_ReadySubtitle");

        // Check if DNS is stuck on 127.0.0.1 (DNSCrypt) but dnscrypt-proxy is not running.
        // This can happen after a reboot if the service failed to start.
        // If so, try to restart it or fall back to Google DNS to restore internet connectivity.
        await _dnscryptManager.CheckAndRecoverDnsAsync();

        var isPoisoned = await _dnsManager.CheckDnsPoisoningSilentAsync();
        if (isPoisoned)
        {
            var result = DarkMessageBox.Show(
                LocalizationService.Get("Msg_DnsPoisonFound"),
                LocalizationService.Get("Title_DnsThreat"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                TxtDashboardSubtitle.Text = "Google DNS...";
                await _dnsManager.SetGoogleDnsAsync();
                await Task.Delay(1000);
            }
        }

        // Add a small delay to ensure background processes (like schtasks/winws2) 
        // have had enough time to spin up on OS startup, preventing false negatives 
        // that cause the UI to flicker "Not Installed" for a split second.
        await Task.Delay(2000);

        await RefreshDashboardAsync();
    }

    private async Task RefreshDashboardAsync()
    {
        if (_isInitializing)
        {
            TxtRoutingStatus.Text = LocalizationService.Get("Status_Loading");
            TxtServiceStatus.Text = LocalizationService.Get("Status_Loading");
            TxtDnsStatus.Text = LocalizationService.Get("Status_Loading");
            TxtLanStatus.Text = LocalizationService.Get("Status_Loading");

            var grayBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9CA3AF"));
            DotRouting.Fill = grayBrush;
            DotService.Fill = grayBrush;
            DotDns.Fill = grayBrush;
            DotLan.Fill = grayBrush;
        }

        // Periodic DNS health check: if DNS is set to 127.0.0.1 (DNSCrypt), verify it's actually resolving
        // This auto-recovers if DNSCrypt silently stops working (e.g. upstream connection lost)
        try { await _dnscryptManager.PeriodicDnsHealthCheckAsync(); } catch { }

        var isProcessRunning = _processManager.IsRunning;
        var isServiceRunning = await _serviceManager.IsServiceRunningAsync();
        var isServiceInstalled = await _serviceManager.IsServiceInstalledAsync();
        var isDnscryptRunning = await _dnscryptManager.IsInstalledAndRunningAsync();
        var isLanActive = _lanShareManager.IsSharingActive;

        if (_isInitializing)
        {
            BtnQuickStart.IsEnabled = false;
            BtnQuickStart.Content = LocalizationService.Get("Status_Loading");
            BtnQuickService.IsEnabled = false;
            BtnQuickService.Content = LocalizationService.Get("Status_Loading");

            TxtRoutingStatus.Text = LocalizationService.Get("Status_Loading");
            TxtRoutingStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9CA3AF"));
            DotRouting.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B7280"));
        }
        else if (isServiceRunning || isServiceInstalled)
        {
            TxtRoutingStatus.Text = isServiceRunning ? LocalizationService.Get("Status_ServiceActive") : LocalizationService.Get("Status_Installed");
            TxtRoutingStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isServiceRunning ? "#10B981" : "#F59E0B"));
            DotRouting.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isServiceRunning ? "#10B981" : "#F59E0B"));
            BtnQuickStart.IsEnabled = false;
            BtnQuickStart.Content = LocalizationService.Get("Btn_StartZapret");
            BtnQuickStart.Style = (Style)FindResource("PrimaryButton");
            BtnQuickService.Content = LocalizationService.Get("Btn_RemoveService");
            BtnQuickService.Style = (Style)FindResource("DangerButton");
            BtnQuickService.IsEnabled = true;
        }
        else if (isProcessRunning)
        {
            TxtRoutingStatus.Text = LocalizationService.Get("Status_ManualActive");
            TxtRoutingStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
            DotRouting.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
            BtnQuickStart.Content = LocalizationService.Get("Btn_StopZapret");
            BtnQuickStart.Style = (Style)FindResource("DangerButton");
            BtnQuickStart.IsEnabled = true;
            BtnQuickService.Content = LocalizationService.Get("Btn_InstallService");
            BtnQuickService.Style = (Style)FindResource("ModernButton");
            BtnQuickService.IsEnabled = false;
        }
        else
        {
            TxtRoutingStatus.Text = LocalizationService.Get("Status_NotInstalled");
            TxtRoutingStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9CA3AF"));
            DotRouting.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B7280"));
            BtnQuickStart.Content = LocalizationService.Get("Btn_StartZapret");
            BtnQuickStart.Style = (Style)FindResource("PrimaryButton");
            BtnQuickStart.IsEnabled = true;
            BtnQuickService.Content = LocalizationService.Get("Btn_InstallService");
            BtnQuickService.Style = (Style)FindResource("ModernButton");
            BtnQuickService.IsEnabled = true;
        }

        TxtEngineStatus.Text = "Zapret2 (LUA)";
        if (TxtFooterVersionValue != null) TxtFooterVersionValue.Text = UpdateService.CurrentVersion;


        if (_isInitializing)
        {
            TxtServiceStatus.Text = LocalizationService.Get("Status_Loading");
            DotService.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B7280"));
        }
        else
        {
            int runningServicesCount = (isServiceInstalled ? 1 : 0) + (isDnscryptRunning ? 1 : 0);
            if (runningServicesCount > 1)
            {
                TxtServiceStatus.Text = string.Format(LocalizationService.Get("Status_MultipleServices"), runningServicesCount);
                DotService.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
            }
            else if (isServiceInstalled)
            {
                TxtServiceStatus.Text = LocalizationService.Get("Status_OneServiceZapret");
                DotService.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
            }
            else if (isDnscryptRunning)
            {
                TxtServiceStatus.Text = LocalizationService.Get("Status_OneServiceDnscrypt");
                DotService.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
            }
            else
            {
                TxtServiceStatus.Text = isProcessRunning ? LocalizationService.Get("Status_OneProcess") : LocalizationService.Get("Status_ZeroService");
                DotService.Fill = isProcessRunning
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"))
                    : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B7280"));
            }
        }

        if (_isInitializing)
        {
            BtnQuickDnscrypt.IsEnabled = false;
            BtnQuickDnscrypt.Content = LocalizationService.Get("Status_Loading");

            TxtDnsStatus.Text = LocalizationService.Get("Status_Loading");
            TxtDnsStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9CA3AF"));
            DotDns.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B7280"));
        }
        else if (isDnscryptRunning)
        {
            TxtDnsStatus.Text = _dnsManager.GetCurrentDnsSummary(isDnscryptRunning);
            DotDns.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
            BtnQuickDnscrypt.Content = LocalizationService.Get("Btn_RemoveDnscrypt");
            BtnQuickDnscrypt.Style = (Style)FindResource("DangerButton");
            BtnQuickDnscrypt.IsEnabled = true;
        }
        else
        {
            var currentDns = _dnsManager.GetCurrentDnsSummary(isDnscryptRunning);
            TxtDnsStatus.Text = currentDns;

            // Eğer 127.0.0.1 ise ama kapalıysa, sarı/kırmızı yanmalı.
            bool isDnsWarning = currentDns.Contains("⚠️");
            DotDns.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isDnsWarning ? "#EF4444" : "#10B981"));
            TxtDnsStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isDnsWarning ? "#EF4444" : "#E5E7EB"));

            BtnQuickDnscrypt.Content = LocalizationService.Get("Btn_InstallDnscrypt");
            BtnQuickDnscrypt.Style = (Style)FindResource("ModernButton");
            BtnQuickDnscrypt.IsEnabled = true;
        }

        TxtWinDivertStatus.Text = LocalizationService.Get("Status_Installed");
        DotWinDivert.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));

        if (_isInitializing)
        {
            BtnToggleLanShare.IsEnabled = false;
            BtnToggleLanShare.Content = LocalizationService.Get("Status_Loading");

            TxtLanStatus.Text = LocalizationService.Get("Status_Loading");
            DotLan.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B7280"));
        }
        else if (isLanActive)
        {
            TxtLanStatus.Text = LocalizationService.Get("Status_Active");
            DotLan.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
            BtnToggleLanShare.Content = LocalizationService.Get("Btn_StopLan");
            BtnToggleLanShare.Style = (Style)FindResource("DangerButton");
            BtnToggleLanShare.IsEnabled = true;
        }
        else
        {
            TxtLanStatus.Text = LocalizationService.Get("Status_Off");
            DotLan.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B7280"));
            BtnToggleLanShare.Content = LocalizationService.Get("Btn_StartLan");
            BtnToggleLanShare.Style = (Style)FindResource("PrimaryButton");
            BtnToggleLanShare.IsEnabled = true;
        }

        if (isServiceRunning)
            TxtDashboardSubtitle.Text = LocalizationService.Get("Overview_ServiceSubtitle");
        else if (isProcessRunning)
            TxtDashboardSubtitle.Text = LocalizationService.Get("Overview_ProcessSubtitle");
        else
            TxtDashboardSubtitle.Text = LocalizationService.Get("Overview_ReadySubtitle");
    }

    private void ProcessManager_ProcessCrashed(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            DarkMessageBox.Show(LocalizationService.Get("Msg_ZapretCrash"), LocalizationService.Get("Title_CrashWarning"), MessageBoxButton.OK, MessageBoxImage.Warning);
            _ = RefreshDashboardAsync();
        });
    }


    private async void BtnQuickStart_Click(object sender, RoutedEventArgs e)
    {
        BtnQuickStart.IsEnabled = false;
        BtnQuickService.IsEnabled = false;
        var prevCursor = Mouse.OverrideCursor;
        Mouse.OverrideCursor = Cursors.Wait;

        try
        {
            if (_processManager.IsRunning)
            {
                BtnQuickStart.Content = LocalizationService.Get("Status_Stopping");
                TxtDashboardSubtitle.Text = LocalizationService.Get("Overview_StoppingSubtitle");
                await _processManager.StopAsync();
            }
            else
            {
                BtnQuickStart.Content = LocalizationService.Get("Status_Starting");
                var stratName = _appConfig.ActiveStrategy;
                var strategyStr = _config.GetPresetStrategyString(stratName);

                TxtDashboardSubtitle.Text = LocalizationService.Get("Overview_StartingSubtitle");
                var ok = await _processManager.StartAsync(strategyStr, _appConfig.FilterMode);

                if (!ok)
                {
                    await RefreshDashboardAsync();
                    DarkMessageBox.Show(LocalizationService.Get("Msg_ZapretStartFail"), LocalizationService.Get("Dialog_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            if (ex.Message == LocalizationService.Get("Msg_ConflictDetected"))
            {
                var res = DarkMessageBox.Show(ex.Message, LocalizationService.Get("Dialog_Error"), MessageBoxButton.YesNo, MessageBoxImage.Error);
                if (res == MessageBoxResult.Yes)
                {
                    Process.Start(new ProcessStartInfo("shutdown", "/r /t 0") { CreateNoWindow = true, UseShellExecute = false });
                }
            }
            else
            {
                DarkMessageBox.Show(ex.Message, LocalizationService.Get("Dialog_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally
        {
            Mouse.OverrideCursor = prevCursor;
            await RefreshDashboardAsync();
        }
    }

    private async void BtnQuickService_Click(object sender, RoutedEventArgs e)
    {
        BtnQuickService.IsEnabled = false;
        BtnQuickStart.IsEnabled = false;
        var prevCursor = Mouse.OverrideCursor;
        Mouse.OverrideCursor = Cursors.Wait;

        try
        {
            var isServiceRunning = await _serviceManager.IsServiceRunningAsync();
            if (isServiceRunning)
            {
                BtnQuickService.Content = LocalizationService.Get("Status_Removing");
                TxtDashboardSubtitle.Text = LocalizationService.Get("Overview_StoppingSubtitle");
                await _serviceManager.RemoveServiceAsync();
                await RefreshDashboardAsync();
                DarkMessageBox.Show(LocalizationService.Get("Msg_ServiceRemoved"), LocalizationService.Get("Dialog_Info"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                BtnQuickService.Content = LocalizationService.Get("Status_Installing");
                if (_processManager.IsRunning)
                {
                    await _processManager.StopAsync();
                }

                var stratName = _appConfig.ActiveStrategy;
                var strategyStr = _config.GetPresetStrategyString(stratName);

                TxtDashboardSubtitle.Text = LocalizationService.Get("Overview_StartingSubtitle");
                var ok = await _serviceManager.InstallServiceAsync(strategyStr, _appConfig.FilterMode);
                await RefreshDashboardAsync();

                if (ok)
                {
                    DarkMessageBox.Show(LocalizationService.Get("Msg_ServiceInstalled"), LocalizationService.Get("Dialog_Success"), MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    DarkMessageBox.Show(LocalizationService.Get("Msg_ServiceStartFail"), LocalizationService.Get("Dialog_Warning"), MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
        catch (Exception ex)
        {
            if (ex.Message == LocalizationService.Get("Msg_ConflictDetected"))
            {
                var res = DarkMessageBox.Show(ex.Message, LocalizationService.Get("Dialog_Error"), MessageBoxButton.YesNo, MessageBoxImage.Error);
                if (res == MessageBoxResult.Yes)
                {
                    Process.Start(new ProcessStartInfo("shutdown", "/r /t 0") { CreateNoWindow = true, UseShellExecute = false });
                }
            }
            else
            {
                DarkMessageBox.Show(ex.Message, LocalizationService.Get("Dialog_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally
        {
            Mouse.OverrideCursor = prevCursor;
            await RefreshDashboardAsync();
        }
    }

    private async void BtnQuickDnscrypt_Click(object sender, RoutedEventArgs e)
    {
        BtnQuickDnscrypt.IsEnabled = false;
        var prevCursor = Mouse.OverrideCursor;
        Mouse.OverrideCursor = Cursors.Wait;

        try
        {
            var isDnscryptRunning = await _dnscryptManager.IsInstalledAndRunningAsync();
            if (isDnscryptRunning)
            {
                BtnQuickDnscrypt.Content = LocalizationService.Get("Status_Removing");
                TxtDashboardSubtitle.Text = LocalizationService.Get("Overview_StoppingSubtitle");
                await _dnscryptManager.StopAndUninstallAsync();
                await RefreshDashboardAsync();
                DarkMessageBox.Show(LocalizationService.Get("Msg_DnscryptRemoved"), LocalizationService.Get("Dialog_Info"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                BtnQuickDnscrypt.Content = LocalizationService.Get("Status_Installing");
                TxtDashboardSubtitle.Text = LocalizationService.Get("Overview_StartingSubtitle");
                var ok = await _dnscryptManager.InstallAndStartAsync();
                await RefreshDashboardAsync();

                if (ok)
                {
                    DarkMessageBox.Show(LocalizationService.Get("Msg_DnscryptInstalled"), LocalizationService.Get("Dialog_Success"), MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    DarkMessageBox.Show(LocalizationService.Get("Msg_DnscryptFail"), LocalizationService.Get("Dialog_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        catch (Exception ex)
        {
            DarkMessageBox.Show(ex.Message, LocalizationService.Get("Dialog_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = prevCursor;
            await RefreshDashboardAsync();
        }
    }


    private void PopulateStrategyPresets()
    {
        _isSyncingStrategy = true;
        try
        {
            var presets = _config.GetAvailablePresets();

            if (CmbStrategyPreset != null)
            {
                CmbStrategyPreset.Items.Clear();
                foreach (var p in presets) CmbStrategyPreset.Items.Add(p);

                var idx = -1;
                for (int i = 0; i < CmbStrategyPreset.Items.Count; i++)
                {
                    if (CmbStrategyPreset.Items[i]?.ToString() == _appConfig.ActiveStrategy)
                    {
                        idx = i;
                        break;
                    }
                }
                CmbStrategyPreset.SelectedIndex = idx >= 0 ? idx : 0;

                if (CmbStrategyPreset.SelectedItem != null && TxtStrategyParams != null)
                {
                    var sel = CmbStrategyPreset.SelectedItem.ToString() ?? "";
                    TxtStrategyParams.Text = _config.GetPresetStrategyString(sel);
                }
            }

            if (CmbOverviewStrategy != null)
            {
                CmbOverviewStrategy.Items.Clear();
                foreach (var p in presets) CmbOverviewStrategy.Items.Add(p);

                var idx = -1;
                for (int i = 0; i < CmbOverviewStrategy.Items.Count; i++)
                {
                    if (CmbOverviewStrategy.Items[i]?.ToString() == _appConfig.ActiveStrategy)
                    {
                        idx = i;
                        break;
                    }
                }
                CmbOverviewStrategy.SelectedIndex = idx >= 0 ? idx : 0;
            }
        }
        finally
        {
            _isSyncingStrategy = false;
        }
    }

    private bool _isSyncingStrategy = false;

    private async Task ApplyActiveRulesLiveAsync()
    {
        var stratName = _appConfig.ActiveStrategy;
        var strategyStr = _config.GetPresetStrategyString(stratName);

        if (_processManager.IsRunning)
        {
            await _processManager.StopAsync();
            await _processManager.StartAsync(strategyStr, _appConfig.FilterMode);
            await RefreshDashboardAsync();
        }
        else if (await _serviceManager.IsServiceRunningAsync())
        {
            await _serviceManager.InstallServiceAsync(strategyStr, _appConfig.FilterMode);
            await RefreshDashboardAsync();
        }
    }

    private async void CmbStrategyPreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingStrategy || CmbStrategyPreset?.SelectedItem is null) return;
        _isSyncingStrategy = true;
        try
        {
            var sel = CmbStrategyPreset.SelectedItem.ToString() ?? "";

            if (TxtStrategyParams != null)
            {
                TxtStrategyParams.Text = _config.GetPresetStrategyString(sel);
            }

            if (BtnDeleteStrategy != null)
            {
                var isActive = sel == _appConfig.ActiveStrategy;
                // Enable delete if it is NOT the currently active profile
                BtnDeleteStrategy.IsEnabled = !isActive;
            }
        }
        finally
        {
            _isSyncingStrategy = false;
        }
    }

    private async void CmbOverviewStrategy_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingStrategy || CmbOverviewStrategy?.SelectedItem is null) return;
        _isSyncingStrategy = true;
        try
        {
            var sel = CmbOverviewStrategy.SelectedItem.ToString() ?? "";
            _appConfig.ActiveStrategy = sel;
            _config.SaveConfig(_appConfig);

            if (CmbStrategyPreset != null && CmbStrategyPreset.SelectedItem?.ToString() != sel)
            {
                CmbStrategyPreset.SelectedItem = sel;
            }

            if (TxtStrategyParams != null)
            {
                TxtStrategyParams.Text = _config.GetPresetStrategyString(sel);
            }

            if (BtnDeleteStrategy != null)
            {
                BtnDeleteStrategy.IsEnabled = false; // Always false here because sel == _appConfig.ActiveStrategy
            }

            await ApplyActiveRulesLiveAsync();
        }
        finally
        {
            _isSyncingStrategy = false;
        }
    }

    private async void BtnSaveStrategy_Click(object sender, RoutedEventArgs e)
    {
        var sel = CmbStrategyPreset.SelectedItem?.ToString() ?? "";
        var defaultName = sel.Replace($"[{LocalizationService.Get("Preset_Custom")}] ", "").Replace(LocalizationService.Get("Preset_ManualCustom"), "").Trim();

        var dialogTitle = LocalizationService.Get("Msg_SaveStrategyTitle") ?? "Stratejiyi Kaydet";
        var dialogPrompt = LocalizationService.Get("Msg_SaveStrategyPrompt") ?? "Bu strateji için bir profil ismi girin:";
        
        var dialog = new ZapretDPI.Views.InputDialog(dialogTitle, dialogPrompt, defaultName) 
        { 
            Owner = this 
        };

        if (dialog.ShowDialog() == true)
        {
            var profileName = dialog.InputText.Trim();
            if (string.IsNullOrEmpty(profileName)) return;

            var profile = new ZapretDPI.Models.CustomProfile
            {
                Name = profileName,
                Parameters = TxtStrategyParams.Text
            };

            var profiles = _config.LoadCustomProfiles();
            profiles.RemoveAll(p => p.Name == profile.Name);
            profiles.Add(profile);
            _config.SaveCustomProfiles(profiles);

            var customPresetKey = $"[{LocalizationService.Get("Preset_Custom")}] {profile.Name}";
            _appConfig.ActiveStrategy = customPresetKey;
            _config.SaveConfig(_appConfig);

            _config.SaveStrategyFile(profile.Parameters);

            PopulateStrategyPresets();

            CmbStrategyPreset.SelectedItem = customPresetKey;

            DarkMessageBox.Show(LocalizationService.Get("Msg_ProfileSaved") ?? "Profil başarıyla kaydedildi.", LocalizationService.Get("Dialog_Success"), MessageBoxButton.OK, MessageBoxImage.Information);
            await ApplyActiveRulesLiveAsync();
        }
    }

    private void BtnDeleteStrategy_Click(object sender, RoutedEventArgs e)
    {
        var sel = CmbStrategyPreset.SelectedItem?.ToString() ?? "";
        if (sel == _appConfig.ActiveStrategy) return;

        var res = DarkMessageBox.Show(
            string.Format(LocalizationService.Get("Msg_ProfileDeleteConfirm") ?? "'{0}' profilini silmek istediğinize emin misiniz?", sel),
            LocalizationService.Get("Msg_ProfileDeleteTitle") ?? "Profili Sil",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        
        if (res == MessageBoxResult.Yes)
        {
            var customPrefix = $"[{LocalizationService.Get("Preset_Custom")}] ";
            if (sel.StartsWith(customPrefix))
            {
                var profileName = sel.Substring(customPrefix.Length);
                var profiles = _config.LoadCustomProfiles();
                profiles.RemoveAll(p => p.Name == profileName);
                _config.SaveCustomProfiles(profiles);
            }
            else
            {
                _appConfig.DeletedPresets ??= new List<string>();
                if (!_appConfig.DeletedPresets.Contains(sel))
                {
                    _appConfig.DeletedPresets.Add(sel);
                    _config.SaveConfig(_appConfig);
                }
            }

            PopulateStrategyPresets();
            CmbStrategyPreset.SelectedItem = LocalizationService.Get("Preset_ManualCustom");
            DarkMessageBox.Show(
                LocalizationService.Get("Msg_ProfileDeleted") ?? "Profil başarıyla silindi.",
                LocalizationService.Get("Dialog_Success"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void BtnRestoreDefaults_Click(object sender, RoutedEventArgs e)
    {
        var res = DarkMessageBox.Show(
            LocalizationService.Get("Msg_DefaultsRestoredConfirm") ?? "Silinen varsayılan profilleri geri yüklemek istediğinize emin misiniz?",
            LocalizationService.Get("Msg_DefaultsRestoredTitle") ?? "Varsayılanları Geri Yükle",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
            
        if (res == MessageBoxResult.Yes)
        {
            if (_appConfig.DeletedPresets != null)
            {
                _appConfig.DeletedPresets.Clear();
                _config.SaveConfig(_appConfig);
                PopulateStrategyPresets();
                DarkMessageBox.Show(
                    LocalizationService.Get("Msg_DefaultsRestoredSuccess") ?? "Varsayılan profiller başarıyla geri yüklendi.",
                    LocalizationService.Get("Dialog_Success"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
    }


    private void LoadFilesToEditors()
    {
        if (File.Exists(_config.GetPath(ConfigFile.Hostlist)))
            TxtHostlist.Text = File.ReadAllText(_config.GetPath(ConfigFile.Hostlist));

        if (File.Exists(_config.GetPath(ConfigFile.ExcludeList)))
            TxtExcludelist.Text = File.ReadAllText(_config.GetPath(ConfigFile.ExcludeList));
    }

    private void BtnSaveHostlist_Click(object sender, RoutedEventArgs e)
    {
        File.WriteAllText(_config.GetPath(ConfigFile.Hostlist), TxtHostlist.Text.Trim());
        DarkMessageBox.Show(LocalizationService.Get("Msg_HostlistSaved"), LocalizationService.Get("Dialog_Info"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnSaveExcludelist_Click(object sender, RoutedEventArgs e)
    {
        File.WriteAllText(_config.GetPath(ConfigFile.ExcludeList), TxtExcludelist.Text.Trim());
        DarkMessageBox.Show(LocalizationService.Get("Msg_ExcludelistSaved"), LocalizationService.Get("Dialog_Info"), MessageBoxButton.OK, MessageBoxImage.Information);
    }


    private async void BtnTestDnsPoison_Click(object sender, RoutedEventArgs e)
    {
        BtnTestDnsPoison.IsEnabled = false;
        BtnTestDnsPoison.Content = LocalizationService.Get("Status_Testing");

        try
        {
            var diag = await _dnsManager.RunLiveDnsDiagnosticAsync();

            var timeStr = string.Format(LocalizationService.Get("Dns_TotalResponseTime"), diag.ElapsedMs);
            var detailsHeader = LocalizationService.Get("Dns_DomainAnalysisDetails");

            var fullReport = $"{diag.Summary}\r\n\r\n" +
                             $"⏱️ {timeStr}\r\n\r\n" +
                             $"--- {detailsHeader} ---\r\n" +
                             $"{diag.Details}";

            if (diag.IsPoisoned)
            {
                DarkMessageBox.Show(fullReport, LocalizationService.Get("Title_DnsThreat"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                DarkMessageBox.Show(fullReport, LocalizationService.Get("Title_DnsReport"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        finally
        {
            BtnTestDnsPoison.IsEnabled = true;
            BtnTestDnsPoison.Content = LocalizationService.Get("Btn_TestDns");
        }
    }

    private async Task<bool> CheckAndWarnDnscryptConflictAsync()
    {
        if (await _dnscryptManager.IsInstalledAndRunningAsync())
        {
            var res = DarkMessageBox.Show(
                LocalizationService.Get("Msg_DnscryptConflict"),
                LocalizationService.Get("Dialog_Warning"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (res == MessageBoxResult.Yes)
            {
                await _dnscryptManager.StopAndUninstallAsync();
                BtnQuickDnscrypt.Content = LocalizationService.Get("Btn_InstallDnscrypt");
                return true;
            }
            return false;
        }
        return true;
    }

    private async void BtnSetGoogleDns_Click(object sender, RoutedEventArgs e)
    {
        BtnSetGoogleDns.IsEnabled = false;
        var prevCursor = Mouse.OverrideCursor;
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            if (await _dnscryptManager.IsInstalledAndRunningAsync())
            {
                if (DataContext is MainViewModel vm) vm.SelectedDnscryptServer = "Google";
                else await _dnscryptManager.ChangeServerAsync("Google");

                await RefreshDashboardAsync();
                DarkMessageBox.Show(LocalizationService.Get("Msg_DnscryptServerChanged") ?? "DNSCrypt sunucusu Google olarak değiştirildi.", LocalizationService.Get("Dialog_Success"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!await CheckAndWarnDnscryptConflictAsync()) return;

            await _dnsManager.SetGoogleDnsAsync();
            await RefreshDashboardAsync();
            if (TxtDnsStatus.Text.Contains("Google") || TxtDnsStatus.Text.Contains("8.8."))
                DarkMessageBox.Show(LocalizationService.Get("Msg_DnsGoogleSet"), LocalizationService.Get("Dialog_Success"), MessageBoxButton.OK, MessageBoxImage.Information);
            else
                DarkMessageBox.Show(LocalizationService.Get("Msg_DnsChangeFail") ?? "Hata: DNS değiştirilemedi (Antivirüs veya yönetici yetkisi engelliyor olabilir).", LocalizationService.Get("Dialog_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnSetGoogleDns.IsEnabled = true;
            Mouse.OverrideCursor = prevCursor;
        }
    }

    private async void BtnSetAdguardDns_Click(object sender, RoutedEventArgs e)
    {
        BtnSetAdguardDns.IsEnabled = false;
        var prevCursor = Mouse.OverrideCursor;
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            if (await _dnscryptManager.IsInstalledAndRunningAsync())
            {
                if (DataContext is MainViewModel vm) vm.SelectedDnscryptServer = "AdGuard-DNSCrypt";
                else await _dnscryptManager.ChangeServerAsync("AdGuard-DNSCrypt");

                await RefreshDashboardAsync();
                DarkMessageBox.Show(LocalizationService.Get("Msg_DnscryptServerChanged") ?? "DNSCrypt sunucusu AdGuard olarak değiştirildi.", LocalizationService.Get("Dialog_Success"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!await CheckAndWarnDnscryptConflictAsync()) return;

            await _dnsManager.SetAdguardDnsAsync();
            await RefreshDashboardAsync();
            if (TxtDnsStatus.Text.Contains("AdGuard") || TxtDnsStatus.Text.Contains("94.140."))
                DarkMessageBox.Show(LocalizationService.Get("Msg_DnsAdguardSet"), LocalizationService.Get("Dialog_Success"), MessageBoxButton.OK, MessageBoxImage.Information);
            else
                DarkMessageBox.Show(LocalizationService.Get("Msg_DnsChangeFail") ?? "Hata: DNS değiştirilemedi (Antivirüs veya yönetici yetkisi engelliyor olabilir).", LocalizationService.Get("Dialog_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnSetAdguardDns.IsEnabled = true;
            Mouse.OverrideCursor = prevCursor;
        }
    }

    private async void BtnSetCloudflareDns_Click(object sender, RoutedEventArgs e)
    {
        BtnSetCloudflareDns.IsEnabled = false;
        var prevCursor = Mouse.OverrideCursor;
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            if (await _dnscryptManager.IsInstalledAndRunningAsync())
            {
                if (DataContext is MainViewModel vm) vm.SelectedDnscryptServer = "Cloudflare";
                else await _dnscryptManager.ChangeServerAsync("Cloudflare");

                await RefreshDashboardAsync();
                DarkMessageBox.Show(LocalizationService.Get("Msg_DnscryptServerChanged") ?? "DNSCrypt sunucusu Cloudflare olarak değiştirildi.", LocalizationService.Get("Dialog_Success"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!await CheckAndWarnDnscryptConflictAsync()) return;

            await _dnsManager.SetCloudflareDnsAsync();
            await RefreshDashboardAsync();
            if (TxtDnsStatus.Text.Contains("Cloudflare") || TxtDnsStatus.Text.Contains("1.1.1."))
                DarkMessageBox.Show(LocalizationService.Get("Msg_DnsCloudflareSet"), LocalizationService.Get("Dialog_Success"), MessageBoxButton.OK, MessageBoxImage.Information);
            else
                DarkMessageBox.Show(LocalizationService.Get("Msg_DnsChangeFail") ?? "Hata: DNS değiştirilemedi (Antivirüs veya yönetici yetkisi engelliyor olabilir).", LocalizationService.Get("Dialog_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnSetCloudflareDns.IsEnabled = true;
            Mouse.OverrideCursor = prevCursor;
        }
    }

    private async void BtnSetQuad9Dns_Click(object sender, RoutedEventArgs e)
    {
        BtnSetQuad9Dns.IsEnabled = false;
        var prevCursor = Mouse.OverrideCursor;
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            if (await _dnscryptManager.IsInstalledAndRunningAsync())
            {
                if (DataContext is MainViewModel vm) vm.SelectedDnscryptServer = "Quad9";
                else await _dnscryptManager.ChangeServerAsync("Quad9");

                await RefreshDashboardAsync();
                DarkMessageBox.Show(LocalizationService.Get("Msg_DnscryptServerChanged") ?? "DNSCrypt sunucusu Quad9 olarak değiştirildi.", LocalizationService.Get("Dialog_Success"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!await CheckAndWarnDnscryptConflictAsync()) return;

            await _dnsManager.SetQuad9DnsAsync();
            await RefreshDashboardAsync();
            if (TxtDnsStatus.Text.Contains("Quad9") || TxtDnsStatus.Text.Contains("9.9.9."))
                DarkMessageBox.Show(LocalizationService.Get("Msg_DnsQuad9Set"), LocalizationService.Get("Dialog_Success"), MessageBoxButton.OK, MessageBoxImage.Information);
            else
                DarkMessageBox.Show(LocalizationService.Get("Msg_DnsChangeFail") ?? "Hata: DNS değiştirilemedi (Antivirüs veya yönetici yetkisi engelliyor olabilir).", LocalizationService.Get("Dialog_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnSetQuad9Dns.IsEnabled = true;
            Mouse.OverrideCursor = prevCursor;
        }
    }

    private async void BtnResetDnsDhcp_Click(object sender, RoutedEventArgs e)
    {
        BtnResetDnsDhcp.IsEnabled = false;
        var prevCursor = Mouse.OverrideCursor;
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            if (!await CheckAndWarnDnscryptConflictAsync()) return;

            await _dnsManager.ResetToDhcpDnsAsync();
            await RefreshDashboardAsync();
            if (TxtDnsStatus.Text.Contains("Otomatik") || TxtDnsStatus.Text.Contains("DHCP") || TxtDnsStatus.Text.Contains("Router") || TxtDnsStatus.Text.Contains("192.168."))
                DarkMessageBox.Show(LocalizationService.Get("Msg_DnsDhcpSet"), LocalizationService.Get("Dialog_Info"), MessageBoxButton.OK, MessageBoxImage.Information);
            else
                DarkMessageBox.Show(LocalizationService.Get("Msg_DnsChangeFail") ?? "Hata: DNS değiştirilemedi (Antivirüs veya yönetici yetkisi engelliyor olabilir).", LocalizationService.Get("Dialog_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnResetDnsDhcp.IsEnabled = true;
            Mouse.OverrideCursor = prevCursor;
        }
    }


    private async void BtnToggleLanShare_Click(object sender, RoutedEventArgs e)
    {
        BtnToggleLanShare.IsEnabled = false;
        var prevCursor = Mouse.OverrideCursor;
        Mouse.OverrideCursor = Cursors.Wait;

        try
        {
            if (_lanShareManager.IsSharingActive)
            {
                BtnToggleLanShare.Content = LocalizationService.Get("Status_Stopping");
                await _lanShareManager.StopSharingAsync();
                await RefreshDashboardAsync();
            }
            else
            {
                if (!_lanShareManager.IsNpcapInstalled())
                {
                    var res = DarkMessageBox.Show(
                        LocalizationService.Get("Msg_NpcapNotFound"),
                        "Npcap",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (res == MessageBoxResult.Yes)
                    {
                        try
                        {
                            Process.Start(new ProcessStartInfo("https://npcap.com/#download") { UseShellExecute = true });
                        }
                        catch { }
                    }
                    return;
                }

                var hasRule = await _lanShareManager.HasFirewallRuleAsync();
                if (!hasRule)
                {
                    DarkMessageBox.Show(LocalizationService.Get("Msg_FirewallPrompt"), LocalizationService.Get("Title_Firewall"), MessageBoxButton.OK, MessageBoxImage.Information);
                }

                BtnToggleLanShare.Content = LocalizationService.Get("Status_Starting");
                var ok = await _lanShareManager.StartSharingAsync();
                await RefreshDashboardAsync();

                if (!ok)
                {
                    DarkMessageBox.Show(LocalizationService.Get("Msg_LanStartFail"), LocalizationService.Get("Dialog_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        catch (Exception ex)
        {
            DarkMessageBox.Show(LocalizationService.Get("Msg_LanStartFail") + "\r\n" + ex.Message, LocalizationService.Get("Dialog_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnToggleLanShare.IsEnabled = true;
            Mouse.OverrideCursor = prevCursor;
            await RefreshDashboardAsync();
        }
    }


    private async void BtnStartAnalysis_Click(object sender, RoutedEventArgs e)
    {
        if (_analysisCts != null)
        {
            BtnStartAnalysis.IsEnabled = false;
            TxtAnalysisStatus.Text = LocalizationService.Get("Analysis_StatusStopping");
            TxtAnalysisLog.AppendText("\r\n[!] " + LocalizationService.Get("Analysis_StatusStopping") + "\r\n");
            _analysisCts.Cancel();
            return;
        }

        if (!System.IO.File.Exists(_config.CygwinBashPath))
        {
            DarkMessageBox.Show(
                "Blockcheck aracını çalıştırmak için gerekli olan 'cygwin' dosyaları bulunamadı. " +
                $"Lütfen 'cygwin' klasörünün şu yolda bulunduğundan emin olun:\n{_config.CygwinBashPath}",
                LocalizationService.Get("Dialog_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _analysisCts = new CancellationTokenSource();
        if (TxtStartAnalysis != null) TxtStartAnalysis.Text = LocalizationService.Get("Btn_StopAnalysis");
        if (EmojiStartAnalysis != null) EmojiStartAnalysis.Text = "⏹️";
        BtnStartAnalysis.Style = (Style)FindResource("DangerButton");
        AnalysisProgress.Visibility = Visibility.Visible;
        TxtAnalysisStatus.Text = LocalizationService.Get("Analysis_StatusPreparing");
        TxtAnalysisLog.Text = "Blockcheck2...\r\n";

        try
        {
            await _processManager.StopAsync();
            await _serviceManager.RemoveServiceAsync();
            await RefreshDashboardAsync();
        }
        catch { }

        TxtAnalysisStatus.Text = LocalizationService.Get("Analysis_StatusRunning");

        var mode = ZapretDPI.Models.ScanMode.Fast;
        if (RbScanSmart.IsChecked == true) mode = ZapretDPI.Models.ScanMode.Smart;
        else if (RbScanDeep.IsChecked == true) mode = ZapretDPI.Models.ScanMode.Deep;

        var progress = new Progress<string>(msg =>
        {
            TxtAnalysisLog.AppendText(msg + "\r\n");
            TxtAnalysisLog.ScrollToEnd();
        });

        List<string>? results = null;
        bool wasCancelled = false;

        try
        {
            results = await _blockcheckRunner.RunBlockcheckAsync(mode, progress, _analysisCts.Token);
        }
        catch (OperationCanceledException)
        {
            wasCancelled = true;
        }
        finally
        {
            _analysisCts?.Dispose();
            _analysisCts = null;
            AnalysisProgress.Visibility = Visibility.Collapsed;
            if (TxtStartAnalysis != null) TxtStartAnalysis.Text = LocalizationService.Get("Btn_StartAnalysis");
            if (EmojiStartAnalysis != null) EmojiStartAnalysis.Text = "▶️";
            BtnStartAnalysis.Style = (Style)FindResource("PrimaryButton");
            BtnStartAnalysis.IsEnabled = true;
        }

        if (wasCancelled)
        {
            TxtAnalysisStatus.Text = LocalizationService.Get("Analysis_StatusStopped");
            return;
        }

        TxtAnalysisStatus.Text = LocalizationService.Get("Analysis_StatusCompleted");

        if (results != null && results.Count > 0)
        {
            TxtAnalysisLog.AppendText($"\r\n=========================\r\n{LocalizationService.Get("Log_FoundStrategies")}\r\n");
            foreach (var r in results) TxtAnalysisLog.AppendText($"{r}\r\n");

            var saveWin = new ProfileSaveWindow(results) { Owner = this };
            if (saveWin.ShowDialog() == true)
            {
                var profile = new ZapretDPI.Models.CustomProfile
                {
                    Name = saveWin.ProfileName!,
                    Parameters = saveWin.SelectedStrategy!
                };

                var profiles = _config.LoadCustomProfiles();
                profiles.RemoveAll(p => p.Name == profile.Name);
                profiles.Add(profile);
                _config.SaveCustomProfiles(profiles);

                var customPresetKey = $"[{LocalizationService.Get("Preset_Custom")}] {profile.Name}";
                _appConfig.ActiveStrategy = customPresetKey;
                _config.SaveConfig(_appConfig);

                _config.SaveStrategyFile(profile.Parameters);

                PopulateStrategyPresets();

                DarkMessageBox.Show(LocalizationService.Get("Msg_ProfileSaved"), LocalizationService.Get("Dialog_Success"), MessageBoxButton.OK, MessageBoxImage.Information);

                if (saveWin.ApplyImmediately)
                {
                    if (_processManager.IsRunning)
                    {
                        await _processManager.StopAsync();
                    }
                    var stratStr = _config.GetPresetStrategyString(customPresetKey);
                    await _processManager.StartAsync(stratStr, _appConfig.FilterMode);
                    await RefreshDashboardAsync();
                }
            }
        }
        else
        {
            DarkMessageBox.Show(LocalizationService.Get("Msg_NoStrategiesFound"), LocalizationService.Get("Dialog_Info"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }


    private async void BtnFullCleanup_Click(object sender, RoutedEventArgs e)
    {
        BtnFullCleanup.IsEnabled = false;
        BtnFullCleanup.Content = LocalizationService.Get("Status_Cleaning");
        var prevCursor = Mouse.OverrideCursor;
        Mouse.OverrideCursor = Cursors.Wait;

        try
        {
            TxtDashboardSubtitle.Text = LocalizationService.Get("Status_Cleaning");

            await _processManager.StopAsync();
            await _serviceManager.RemoveServiceAsync();
            await _dnscryptManager.StopAndUninstallAsync();
            await _lanShareManager.StopSharingAsync();
            await _dnsManager.ResetToDhcpDnsAsync();

            await RefreshDashboardAsync();

            DarkMessageBox.Show(LocalizationService.Get("Msg_CleanupDone"), LocalizationService.Get("Title_CleanupDone"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            DarkMessageBox.Show(ex.Message, LocalizationService.Get("Dialog_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnFullCleanup.IsEnabled = true;
            BtnFullCleanup.Content = LocalizationService.Get("Recovery_BtnCleanup");
            Mouse.OverrideCursor = prevCursor;
            await RefreshDashboardAsync();
        }
    }


    private async void CmbFilterMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbFilterMode is null) return;
        _appConfig.FilterMode = (FilterMode)CmbFilterMode.SelectedIndex;
        _config.SaveConfig(_appConfig);

        await ApplyActiveRulesLiveAsync();
    }

    private void PopulateAboutInfo()
    {
        if (TxtAboutVersionHeader is null) return;
        var curVer = UpdateService.CurrentVersion;
        TxtAboutVersionHeader.Text = curVer;
        if (TxtAboutPlatform != null) TxtAboutPlatform.Text = $"Windows {Environment.OSVersion.Version.Major} ({Environment.OSVersion.Version.Build})";
        if (TxtAboutArch != null) TxtAboutArch.Text = Environment.Is64BitProcess ? "64-Bit (X64)" : "32-Bit (X86)";
        if (TxtAboutRuntime != null) TxtAboutRuntime.Text = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
        if (TxtAboutLanguage != null)
        {
            TxtAboutLanguage.Text = LocalizationService.CurrentLanguage switch
            {
                "tr" => "Türkçe",
                "en" => "English",
                "ru" => "Русский",
                _ => "Türkçe"
            };
        }
        if (TxtAboutCore != null) TxtAboutCore.Text = "Zapret2 v1.0.4 (LUA)";
    }

    private void BtnOpenRepo_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/mattmurdock0010/ZapretDPI-TR",
                UseShellExecute = true
            });
        }
        catch { }
    }
}


