using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace ZapretDPI.Services;

public class LanShareManager
{
    private readonly ConfigService _config;
    private Process? _pcapProcess;

    public bool IsSharingActive => _pcapProcess != null && !_pcapProcess.HasExited;

    public LanShareManager(ConfigService config)
    {
        _config = config;
    }

    public bool IsNpcapInstalled()
    {
        var sysDir = Environment.SystemDirectory;
        return File.Exists(Path.Combine(sysDir, "Packet.dll")) ||
               File.Exists(Path.Combine(sysDir, "Npcap", "Packet.dll")) ||
               File.Exists(Path.Combine(sysDir, "drivers", "npcap.sys"));
    }

    public async Task<bool> HasFirewallRuleAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                var targetExe = _config.GoPcap2SocksExePath;
                var psCmd = $"Get-NetFirewallApplicationFilter -Program '{targetExe}' -ErrorAction SilentlyContinue | Get-NetFirewallRule | Where-Object {{ $_.Action -eq 'Allow' -and $_.Enabled -eq 'True' }}";
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -Command \"{psCmd}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };

                using var p = Process.Start(psi);
                if (p != null)
                {
                    var output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(3000);
                    return output.Length > 10;
                }
            }
            catch { }
            return false;
        });
    }

    public async Task<bool> StartSharingAsync()
    {
        var exePath = _config.GoPcap2SocksExePath;
        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException($"go-pcap2socks bulunamadı: {exePath}");
        }

        await StopSharingAsync();

        return await Task.Run(async () =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = _config.GoPcap2SocksDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                _pcapProcess = Process.Start(psi);
                if (_pcapProcess == null) return false;

                await Task.Delay(600);
                return !_pcapProcess.HasExited;
            }
            catch
            {
                _pcapProcess = null;
                return false;
            }
        });
    }

    public Task StopSharingAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                if (_pcapProcess != null && !_pcapProcess.HasExited)
                {
                    _pcapProcess.Kill(true);
                    _pcapProcess.WaitForExit(1000);
                }
            }
            catch { }
            finally
            {
                _pcapProcess?.Dispose();
                _pcapProcess = null;
            }

            foreach (var p in Process.GetProcessesByName("go-pcap2socks"))
            {
                try
                {
                    p.Kill(true);
                    p.WaitForExit(500);
                }
                catch { }
            }
        });
    }
}
