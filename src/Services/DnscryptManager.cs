using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ZapretDPI.Services;

public class DnscryptManager
{
    private readonly ConfigService _config;
    private readonly DnsManager _dnsManager;
    private Process? _processFallback;

    public event EventHandler? StateChanged;
    public event EventHandler<string>? FallbackOccurred;

    public string TomlPath => Path.Combine(_config.DnscryptDir, "dnscrypt-proxy.toml");

    public DnscryptManager(ConfigService config, DnsManager dnsManager)
    {
        _config = config;
        _dnsManager = dnsManager;
        EnsureTomlExists();
    }

    public void EnsureTomlExists(bool forceUpdate = false)
    {
        if (!forceUpdate && File.Exists(TomlPath))
            return;

        var conf = _config.LoadConfig();
        var serverNameInput = string.IsNullOrWhiteSpace(conf.DnscryptServer) ? "google" : conf.DnscryptServer;
        var isCustom = serverNameInput.StartsWith("sdns://", StringComparison.OrdinalIgnoreCase);
        var serverName = isCustom ? "custom" : serverNameInput.ToLowerInvariant();

        var content = $"server_names = ['{serverName}']\r\n" +
                      "listen_addresses = ['127.0.0.1:53', '[::1]:53']\r\n" +
                      "max_clients = 250\r\n" +
                      "ipv4_servers = true\r\n" +
                      "ipv6_servers = false\r\n" +
                      "dnscrypt_servers = true\r\n" +
                      "doh_servers = true\r\n" +
                      "require_dnssec = false\r\n" +
                      "require_nolog = true\r\n" +
                      "require_nofilter = false\r\n" +
                      "fallback_resolvers = ['8.8.8.8:53', '1.1.1.1:53', '94.140.14.14:53']\r\n" +
                      "netprobe_address = '8.8.8.8:53'\r\n" +
                      "netprobe_timeout = 300\r\n" +
                      "keepalive = 30\r\n" +
                      "block_unqualified = true\r\n" +
                      "block_undelegated = true\r\n" +
                      "log_level = 2\r\n" +
                      "log_file = 'dnscrypt-proxy.log'\r\n" +
                      "log_file_latest = true\r\n" +
                      "cache = true\r\n" +
                      "cache_size = 4096\r\n" +
                      "cache_min_ttl = 2400\r\n" +
                      "cache_max_ttl = 86400\r\n" +
                      "cache_neg_min_ttl = 60\r\n" +
                      "cache_neg_max_ttl = 600\r\n\r\n" +
                      "[static]\r\n" +
                      (isCustom ? $"  [static.'custom']\r\n  stamp = '{serverNameInput}'\r\n\r\n" : "") +
                      "  [static.'adguard-dnscrypt']\r\n" +
                      "  stamp = 'sdns://AQMAAAAAAAAAETk0LjE0MC4xNC4xNDo1NDQzINErR_JS3PLCu_iZEIbq95zkSV2LFsigxDIuUso_OQhzIjIuZG5zY3J5cHQuZGVmYXVsdC5uczEuYWRndWFyZC5jb20'\r\n\r\n" +
                      "  [static.'google']\r\n" +
                      "  stamp = 'sdns://AgAAAAAAAAAABzguOC44LjgACmRucy5nb29nbGUKL2Rucy1xdWVyeQ'\r\n\r\n" +
                      "  [static.'quad9']\r\n" +
                      "  stamp = 'sdns://AgMAAAAAAAAABzkuOS45LjkADWRucy5xdWFkOS5uZXQKL2Rucy1xdWVyeQ'\r\n\r\n" +
                      "  [static.'cloudflare']\r\n" +
                      "  stamp = 'sdns://AgcAAAAAAAAABzEuMS4xLjEAEmRucy5jbG91ZGZsYXJlLmNvbQovZG5zLXF1ZXJ5'\r\n";

        try
        {
            File.WriteAllText(TomlPath, content);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "dnscrypt-proxy.toml yazılamadı");
        }
    }

    public async Task ChangeServerAsync(string serverName)
    {
        var conf = _config.LoadConfig();
        conf.DnscryptServer = serverName;
        _config.SaveConfig(conf);
        EnsureTomlExists(forceUpdate: true);

        if (await IsInstalledAndRunningAsync())
        {
            Serilog.Log.Information($"DNSCrypt sunucusu {serverName} olarak değiştirildi, servis yeniden başlatılıyor...");
            RunCmd("sc.exe stop dnscrypt-proxy", _config.DnscryptDir);
            await Task.Delay(500);
            RunCmd("sc.exe start dnscrypt-proxy", _config.DnscryptDir);
            await Task.Delay(1000);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task<bool> IsInstalledAndRunningAsync()
    {
        return await Task.Run(() =>
        {
            if (_processFallback != null && !_processFallback.HasExited)
                return true;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = "query dnscrypt-proxy",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };

                using var p = Process.Start(psi);
                if (p != null)
                {
                    var output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(2000);
                    if (output.Contains("STATE", StringComparison.OrdinalIgnoreCase) &&
                        (output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase) ||
                         output.Contains("START_PENDING", StringComparison.OrdinalIgnoreCase)))
                    {
                        return true;
                    }
                }
            }
            catch { }

            var procs = Process.GetProcessesByName("dnscrypt-proxy");
            return procs.Length > 0;
        });
    }

    /// <summary>
    /// Checks if DNS is set to 127.0.0.1 (DNSCrypt) but DNS resolution is not actually working.
    /// If so, automatically recovers DNS by setting Google DNS to restore internet connectivity.
    /// Should be called at application startup and periodically while the app is running.
    /// </summary>
    public async Task CheckAndRecoverDnsAsync()
    {
        await Task.Run(async () =>
        {
            try
            {
                // Check if DNS is currently pointing to localhost
                bool isRunning = await IsInstalledAndRunningAsync();
                var currentDns = _dnsManager.GetCurrentDnsSummary(isRunning);
                if (!currentDns.Contains("127.0.0.1") && !currentDns.Contains("DNSCrypt"))
                    return; // DNS is not set to localhost, nothing to recover

                // DNS is set to 127.0.0.1 — verify that DNS resolution actually works
                // Even if dnscrypt-proxy process is "running", it might not be resolving queries
                bool dnsWorking = await TestDnsResolutionAsync();
                if (dnsWorking)
                    return; // DNS resolution is working fine through DNSCrypt

                // DNS resolution is broken! Try to restart dnscrypt-proxy first
                Debug.WriteLine("[DNSCrypt Recovery] DNS is 127.0.0.1 but resolution is failing. Attempting recovery...");

                var exePath = _config.DnscryptExePath;
                if (File.Exists(exePath))
                {
                    // Kill any zombie process and restart
                    foreach (var p in Process.GetProcessesByName("dnscrypt-proxy"))
                    {
                        try { p.Kill(true); p.WaitForExit(1000); } catch { }
                    }
                    await Task.Delay(500);

                    // Regenerate config in case it was corrupted
                    EnsureTomlExists();

                    RunCmd("sc.exe start dnscrypt-proxy", _config.DnscryptDir);
                    await Task.Delay(3000);

                    if (await TestDnsResolutionAsync())
                    {
                        Debug.WriteLine("[DNSCrypt Recovery] Successfully restarted dnscrypt-proxy and DNS is working.");
                        return; // Service restarted successfully
                    }
                }

                // Service couldn't recover — fall back to Google DNS to restore internet
                Debug.WriteLine("[DNSCrypt Recovery] Could not restore DNS via dnscrypt-proxy. Falling back to Google DNS.");
                await _dnsManager.SetGoogleDnsAsync();

                // Notify UI about the fallback
                FallbackOccurred?.Invoke(this, LocalizationService.Get("Msg_DnsFallback") ?? "DNS bağlantı sorunu nedeniyle internetinizi kurtarmak için otomatik olarak Google DNS'e geçildi.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DNSCrypt Recovery Error] {ex.Message}");
                // Last resort: try to reset DNS anyway
                try { await _dnsManager.SetGoogleDnsAsync(); } catch { }
            }
            finally
            {
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    /// <summary>
    /// Tests if DNS resolution is actually working by trying to resolve a well-known domain.
    /// Returns true if resolution succeeds, false otherwise.
    /// </summary>
    private static async Task<bool> TestDnsResolutionAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                var hostEntry = System.Net.Dns.GetHostEntry("www.google.com");
                return hostEntry.AddressList.Length > 0;
            }
            catch
            {
                return false;
            }
        });
    }

    // Track when the last periodic health check was done
    private DateTime _lastHealthCheck = DateTime.MinValue;

    /// <summary>
    /// Lightweight periodic DNS health check. Called from the dashboard refresh timer.
    /// Only runs the full check every 30 seconds to avoid performance overhead.
    /// </summary>
    public async Task PeriodicDnsHealthCheckAsync()
    {
        // Only check every 30 seconds
        if ((DateTime.Now - _lastHealthCheck).TotalSeconds < 30)
            return;

        _lastHealthCheck = DateTime.Now;

        bool isRunning = await IsInstalledAndRunningAsync();
        var currentDns = _dnsManager.GetCurrentDnsSummary(isRunning);
        if (!currentDns.Contains("127.0.0.1") && !currentDns.Contains("DNSCrypt"))
            return; // Not using DNSCrypt, no need to check

        // Quick DNS resolution test
        bool dnsWorking = await TestDnsResolutionAsync();
        if (!dnsWorking)
        {
            Debug.WriteLine("[DNSCrypt Health] Periodic check detected DNS failure. Running recovery...");
            await CheckAndRecoverDnsAsync();
        }
    }

    public async Task<bool> InstallAndStartAsync()
    {
        EnsureTomlExists();
        var exePath = _config.DnscryptExePath;
        var dir = _config.DnscryptDir;

        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException($"DNSCrypt dosyası bulunamadı: {exePath}");
        }

        return await Task.Run(async () =>
        {
            try
            {
                foreach (var p in Process.GetProcessesByName("dnscrypt-proxy"))
                {
                    try { p.Kill(true); p.WaitForExit(500); } catch { }
                }

                RunCmd("sc.exe stop SharedAccess", dir);
                await Task.Delay(500);

                RunCmd("sc.exe start dnscrypt-proxy", dir);
                await Task.Delay(500);

                var isRunning = await IsInstalledAndRunningAsync();
                if (!isRunning)
                {
                    RunCmd($"\"{exePath}\" -config \"{TomlPath}\" -service install", dir);
                    await Task.Delay(300);
                    // Set service dependencies so it starts after network is ready on boot
                    RunCmd("sc.exe config dnscrypt-proxy depend= Tcpip/Dhcp/Dnscache", dir);
                    // Enable automatic crash recovery (Restart after 5 seconds)
                    RunCmd("sc.exe failure dnscrypt-proxy reset= 0 actions= restart/5000/restart/10000//10000", dir);
                    RunCmd($"\"{exePath}\" -service start", dir);
                    await Task.Delay(600);
                    isRunning = await IsInstalledAndRunningAsync();
                }

                if (!isRunning)
                {
                    var fullImagePath = $"\"{exePath}\" -config \"{TomlPath}\"";
                    RunCmd($"sc.exe create dnscrypt-proxy binPath= \"{exePath}\" start= auto DisplayName= \"DNSCrypt Encrypted DNS\"", dir);

                    try
                    {
                        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\dnscrypt-proxy", true);
                        if (key != null)
                        {
                            key.SetValue("ImagePath", fullImagePath, Microsoft.Win32.RegistryValueKind.ExpandString);
                            key.SetValue("Description", "DNSCrypt secure encrypted DNS proxy service");
                        }
                    }
                    catch { }

                    // Set service dependencies so it starts after network is ready on boot
                    RunCmd("sc.exe config dnscrypt-proxy depend= Tcpip/Dhcp/Dnscache", dir);
                    RunCmd("sc.exe start dnscrypt-proxy", dir);
                    await Task.Delay(600);
                    isRunning = await IsInstalledAndRunningAsync();
                }

                if (!isRunning)
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = exePath,
                        Arguments = $"-config \"{TomlPath}\"",
                        WorkingDirectory = dir,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    _processFallback = Process.Start(psi);
                    await Task.Delay(600);
                    isRunning = _processFallback != null && !_processFallback.HasExited;
                }

                for (int i = 0; i < 6; i++)
                {
                    if (await IsInstalledAndRunningAsync())
                    {
                        isRunning = true;
                        break;
                    }
                    await Task.Delay(500);
                }

                if (isRunning)
                {
                    await _dnsManager.SetLocalhostDnsAsync();
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "DNSCrypt Install Error");
                return false;
            }
            finally
            {
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    public async Task<bool> StopAndUninstallAsync()
    {
        var exePath = _config.DnscryptExePath;
        var dir = _config.DnscryptDir;

        return await Task.Run(async () =>
        {
            try
            {
                await _dnsManager.ResetToDhcpDnsAsync();

                if (_processFallback != null)
                {
                    try
                    {
                        if (!_processFallback.HasExited)
                        {
                            _processFallback.Kill(true);
                            _processFallback.WaitForExit(1000);
                        }
                    }
                    catch { }
                    finally
                    {
                        _processFallback?.Dispose();
                        _processFallback = null;
                    }
                }

                RunCmd("sc.exe stop dnscrypt-proxy", dir);
                RunCmd("sc.exe delete dnscrypt-proxy", dir);

                if (File.Exists(exePath))
                {
                    RunCmd($"\"{exePath}\" -service stop", dir);
                    RunCmd($"\"{exePath}\" -service uninstall", dir);
                }

                foreach (var p in Process.GetProcessesByName("dnscrypt-proxy"))
                {
                    try { p.Kill(true); p.WaitForExit(500); } catch { }
                }

                await Task.Delay(500);
                return true;
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "DNSCrypt Uninstall Error");
                return false;
            }
            finally
            {
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    private static string RunCmd(string command, string workingDir)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{command}\"",
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var p = Process.Start(psi);
            if (p != null)
            {
                var output = p.StandardOutput.ReadToEnd() + " " + p.StandardError.ReadToEnd();
                p.WaitForExit(3000);
                return output;
            }
        }
        catch { }
        return string.Empty;
    }
}
