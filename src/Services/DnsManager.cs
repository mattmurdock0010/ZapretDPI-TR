using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace ZapretDPI.Services;

public class DnsDiagnosticResult
{
    public bool IsPoisoned { get; set; }
    public long ElapsedMs { get; set; }
    public string Details { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
}

public class DnsManager
{
    private readonly ConfigService _config;

    public DnsManager(ConfigService config)
    {
        _config = config;
    }

    private static readonly string[] TestDomains = new[]
    {
        "updates.discord.com",
        "discord.gg",
        "roblox.com"
    };

    private const string SafeDiscordPrefix = "162.159.";
    private const string SafeDiscordPrefix2 = "104.16.";
    private const string SafeDiscordPrefix3 = "104.17.";
    private const string SafeRobloxPrefix = "128.116.";
    private const string TtPoisonIpPrefix = "195.175.";

    public async Task<bool> CheckDnsPoisoningSilentAsync()
    {
        var result = await RunLiveDnsDiagnosticAsync();
        return result.IsPoisoned;
    }

    public async Task<DnsDiagnosticResult> RunLiveDnsDiagnosticAsync()
    {
        return await Task.Run(() =>
        {
            var sw = Stopwatch.StartNew();
            var sb = new StringBuilder();
            bool poisoned = false;
            int totalTests = 0;
            int successfulTests = 0;

            foreach (var domain in TestDomains)
            {
                totalTests++;
                var domainSw = Stopwatch.StartNew();
                try
                {
                    var hostEntry = Dns.GetHostEntry(domain);
                    domainSw.Stop();

                    var ipList = string.Join(", ", Array.ConvertAll(hostEntry.AddressList, ip => ip.ToString()));
                    bool domainPoisoned = false;

                    foreach (var ip in hostEntry.AddressList)
                    {
                        var ipStr = ip.ToString();
                        if (ipStr.StartsWith(TtPoisonIpPrefix))
                        {
                            domainPoisoned = true;
                            poisoned = true;
                            break;
                        }
                    }

                    if (domainPoisoned)
                    {
                        var tag = LocalizationService.Get("Dns_PoisonedTag");
                        sb.AppendLine($"❌ {domain} ➔ {ipList} ({domainSw.ElapsedMilliseconds}ms) [{tag}]");
                    }
                    else
                    {
                        successfulTests++;
                        var tag = LocalizationService.Get("Dns_CleanTag");
                        sb.AppendLine($"✓ {domain} ➔ {ipList} ({domainSw.ElapsedMilliseconds}ms) [{tag}]");
                    }
                }
                catch (SocketException ex)
                {
                    domainSw.Stop();
                    var tag = LocalizationService.Get("Dns_UnresolvedTag");
                    sb.AppendLine($"⚠ {domain} ➔ {tag} ({ex.SocketErrorCode}, {domainSw.ElapsedMilliseconds}ms)");
                    poisoned = true;
                }
                catch (Exception ex)
                {
                    domainSw.Stop();
                    sb.AppendLine($"⚠ {domain} ➔ {ex.Message}");
                }
            }

            sw.Stop();

            var summary = poisoned
                ? LocalizationService.Get("Dns_PoisonedSummary")
                : string.Format(LocalizationService.Get("Dns_CleanSummary"), successfulTests, totalTests);

            return new DnsDiagnosticResult
            {
                IsPoisoned = poisoned,
                ElapsedMs = sw.ElapsedMilliseconds,
                Details = sb.ToString().TrimEnd(),
                Summary = summary
            };
        });
    }

    public string GetCurrentDnsSummary(bool isDnscryptRunning)
    {
        try
        {
            var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
            foreach (var ni in interfaces)
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                var desc = ni.Description.ToLowerInvariant();
                if (desc.Contains("virtual") || desc.Contains("windivert") || desc.Contains("npcap")) continue;

                var ipProps = ni.GetIPProperties();
                var dnsAddresses = ipProps.DnsAddresses;
                if (dnsAddresses.Count > 0)
                {
                    var first = dnsAddresses[0].ToString();
                    if (first == "127.0.0.1" || first == "::1")
                    {
                        if (!isDnscryptRunning) return "⚠️ 127.0.0.1 (DNSCrypt Kapalı!)";

                        var conf = _config.LoadConfig();
                        var serverName = string.IsNullOrWhiteSpace(conf.DnscryptServer) ? "Google" : conf.DnscryptServer;

                        // Normalize known server names for display
                        if (serverName.Equals("google", StringComparison.OrdinalIgnoreCase)) serverName = "Google";
                        else if (serverName.Equals("cloudflare", StringComparison.OrdinalIgnoreCase)) serverName = "Cloudflare";
                        else if (serverName.Equals("quad9", StringComparison.OrdinalIgnoreCase)) serverName = "Quad9";
                        else if (serverName.Equals("adguard-dnscrypt", StringComparison.OrdinalIgnoreCase) || serverName.Equals("adguard", StringComparison.OrdinalIgnoreCase)) serverName = "AdGuard";

                        return $"DNSCrypt ({serverName})";
                    }
                    if (first.StartsWith("8.8.")) return "Google DNS (8.8.8.8)";
                    if (first.StartsWith("1.1.1.") || first.StartsWith("1.0.0.")) return "Cloudflare (1.1.1.1)";
                    if (first.StartsWith("94.140.")) return "AdGuard DNS (94.140...)";
                    if (first.StartsWith("9.9.9.") || first.StartsWith("149.112.")) return "Quad9 DNS";
                    if (first.StartsWith("192.168.") || first.StartsWith("10.") || first.StartsWith("172.")) return "Otomatik (DHCP / Router)";
                    return $"Özel DNS ({first})";
                }
            }
        }
        catch { }
        return "Otomatik (DHCP)";
    }

    public async Task SetGoogleDnsAsync()
    {
        var psScript = "Get-NetAdapter | Where-Object { $_.Status -eq 'Up' -and $_.InterfaceDescription -notmatch 'Virtual|WinDivert|Npcap' } | ForEach-Object { Set-DnsClientServerAddress -InterfaceIndex $_.ifIndex -ServerAddresses '8.8.8.8', '8.8.4.4' -ErrorAction SilentlyContinue }; ipconfig /flushdns;";
        await RunPowerShellAsync(psScript);
    }

    public async Task SetAdguardDnsAsync()
    {
        var psScript = "Get-NetAdapter | Where-Object { $_.Status -eq 'Up' -and $_.InterfaceDescription -notmatch 'Virtual|WinDivert|Npcap' } | ForEach-Object { Set-DnsClientServerAddress -InterfaceIndex $_.ifIndex -ServerAddresses '94.140.14.14', '94.140.15.15' -ErrorAction SilentlyContinue }; ipconfig /flushdns;";
        await RunPowerShellAsync(psScript);
    }

    public async Task SetCloudflareDnsAsync()
    {
        var psScript = "Get-NetAdapter | Where-Object { $_.Status -eq 'Up' -and $_.InterfaceDescription -notmatch 'Virtual|WinDivert|Npcap' } | ForEach-Object { Set-DnsClientServerAddress -InterfaceIndex $_.ifIndex -ServerAddresses '1.1.1.1', '1.0.0.1' -ErrorAction SilentlyContinue }; ipconfig /flushdns;";
        await RunPowerShellAsync(psScript);
    }

    public async Task SetQuad9DnsAsync()
    {
        var psScript = "Get-NetAdapter | Where-Object { $_.Status -eq 'Up' -and $_.InterfaceDescription -notmatch 'Virtual|WinDivert|Npcap' } | ForEach-Object { Set-DnsClientServerAddress -InterfaceIndex $_.ifIndex -ServerAddresses '9.9.9.9', '149.112.112.112' -ErrorAction SilentlyContinue }; ipconfig /flushdns;";
        await RunPowerShellAsync(psScript);
    }

    public async Task SetLocalhostDnsAsync()
    {
        var psScript = "Get-NetAdapter | Where-Object { $_.Status -eq 'Up' -and $_.InterfaceDescription -notmatch 'Virtual|WinDivert|Npcap' } | ForEach-Object { Set-DnsClientServerAddress -InterfaceIndex $_.ifIndex -ServerAddresses '127.0.0.1', '::1' -ErrorAction SilentlyContinue }; ipconfig /flushdns;";
        await RunPowerShellAsync(psScript);
    }

    public async Task ResetToDhcpDnsAsync()
    {
        var psScript = "Get-NetAdapter | Where-Object { $_.Status -eq 'Up' -and $_.InterfaceDescription -notmatch 'Virtual|WinDivert|Npcap' } | ForEach-Object { Set-DnsClientServerAddress -InterfaceIndex $_.ifIndex -ResetServerAddresses -ErrorAction SilentlyContinue }; ipconfig /flushdns;";
        await RunPowerShellAsync(psScript);
    }

    private static Task RunPowerShellAsync(string script)
    {
        return Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var p = Process.Start(psi);
                p?.WaitForExit(5000);
            }
            catch { }
        });
    }
}
