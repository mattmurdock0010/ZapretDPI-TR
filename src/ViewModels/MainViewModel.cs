using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Threading.Tasks;
using System.Net.NetworkInformation;
using System.Windows.Threading;
using ZapretDPI.Services;
using ZapretDPI.Models;

namespace ZapretDPI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ConfigService _configService;
    private readonly DnsManager _dnsManager;
    private readonly ZapretProcessManager _processManager;
    private readonly WindowsServiceManager _serviceManager;
    private readonly DnscryptManager _dnscryptManager;
    private readonly BlockcheckRunner _blockcheckRunner;
    private readonly LanShareManager _lanShareManager;
    private readonly UpdateService _updateService;

    [ObservableProperty]
    private string _trafficInfo = "Trafik İzleniyor...";

    [ObservableProperty]
    private bool _isTrafficActive = false;

    [ObservableProperty]
    private string _selectedDnscryptServer = "Google";

    public string[] AvailableDnscryptServers { get; } = new[]
    {
        "Cloudflare",
        "Google",
        "Quad9",
        "AdGuard-DNSCrypt"
    };

    private DispatcherTimer? _trafficTimer;
    private long _lastBytesReceived = 0;
    private long _lastBytesSent = 0;

    public MainViewModel(
        ConfigService configService,
        DnsManager dnsManager,
        ZapretProcessManager processManager,
        WindowsServiceManager serviceManager,
        DnscryptManager dnscryptManager,
        BlockcheckRunner blockcheckRunner,
        LanShareManager lanShareManager,
        UpdateService updateService)
    {
        _configService = configService;
        _dnsManager = dnsManager;
        _processManager = processManager;
        _serviceManager = serviceManager;
        _dnscryptManager = dnscryptManager;
        _blockcheckRunner = blockcheckRunner;
        _lanShareManager = lanShareManager;
        _updateService = updateService;

        var conf = _configService.LoadConfig();
        string loadedServer = string.IsNullOrWhiteSpace(conf.DnscryptServer) ? "Google" : conf.DnscryptServer;

        if (loadedServer.Equals("google", StringComparison.OrdinalIgnoreCase)) loadedServer = "Google";
        else if (loadedServer.Equals("cloudflare", StringComparison.OrdinalIgnoreCase)) loadedServer = "Cloudflare";
        else if (loadedServer.Equals("quad9", StringComparison.OrdinalIgnoreCase)) loadedServer = "Quad9";
        else if (loadedServer.Equals("adguard-dnscrypt", StringComparison.OrdinalIgnoreCase)) loadedServer = "AdGuard-DNSCrypt";
        else if (loadedServer.Equals("adguard", StringComparison.OrdinalIgnoreCase)) loadedServer = "AdGuard-DNSCrypt";

        _selectedDnscryptServer = loadedServer;

        StartTrafficMonitor();
    }

    partial void OnSelectedDnscryptServerChanged(string value)
    {
        _ = _dnscryptManager.ChangeServerAsync(value);
    }

    private void StartTrafficMonitor()
    {
        _trafficTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _trafficTimer.Tick += (s, e) =>
        {
            try
            {
                var stats = IPGlobalProperties.GetIPGlobalProperties().GetIPv4GlobalStatistics();
                long currentReceived = stats.ReceivedPackets;
                long totalBytesReceived = 0;
                long totalBytesSent = 0;

                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus == OperationalStatus.Up &&
                        ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    {
                        var interfaceStats = ni.GetIPStatistics();
                        totalBytesReceived += interfaceStats.BytesReceived;
                        totalBytesSent += interfaceStats.BytesSent;
                    }
                }

                if (_lastBytesReceived == 0)
                {
                    _lastBytesReceived = totalBytesReceived;
                    _lastBytesSent = totalBytesSent;
                    return;
                }

                long dlSpeed = totalBytesReceived - _lastBytesReceived;
                long ulSpeed = totalBytesSent - _lastBytesSent;

                _lastBytesReceived = totalBytesReceived;
                _lastBytesSent = totalBytesSent;

                TrafficInfo = $"↓ {FormatBytes(dlSpeed)}/s  ↑ {FormatBytes(ulSpeed)}/s";
                IsTrafficActive = (dlSpeed > 1000 || ulSpeed > 1000);
            }
            catch
            {
                TrafficInfo = "Trafik Verisi Yok";
            }
        };
        _trafficTimer.Start();
    }

    private string FormatBytes(long bytes)
    {
        if (bytes > 1048576) return (bytes / 1048576.0).ToString("0.0") + " MB";
        if (bytes > 1024) return (bytes / 1024.0).ToString("0") + " KB";
        return bytes + " B";
    }

    [RelayCommand]
    private async Task DeepCleanupAsync()
    {
        try
        {
            await SystemCleanupService.DeepCleanupAsync();
            Views.DarkMessageBox.Show(LocalizationService.Get("Msg_DeepCleanupSuccess"), "Bilgi", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "DeepCleanup UI Error");
            Views.DarkMessageBox.Show($"Hata: {ex.Message}", "Hata", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }
}
