using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using ZapretDPI.Models;

namespace ZapretDPI.Services;

public class ZapretProcessManager
{
    private readonly ConfigService _config;
    private Process? _activeProcess;
    private System.Threading.Timer? _healthTimer;

    public bool IsRunning => _activeProcess != null && !_activeProcess.HasExited;
    public int? ProcessId => _activeProcess?.Id;

    public event EventHandler? ProcessCrashed;
    public event EventHandler? StateChanged;

    public ZapretProcessManager(ConfigService config)
    {
        _config = config;
    }

    public string BuildCommandLine(string strategy, FilterMode filterMode, out string executablePath)
    {
        executablePath = _config.Winws2ExePath;
        var luaDir = _config.LuaDir;
        var luaParams = $" --lua-init=\"@{Path.Combine(luaDir, "zapret-lib.lua")}\"" +
                        $" --lua-init=\"@{Path.Combine(luaDir, "zapret-antidpi.lua")}\"" +
                        $" --lua-init=\"@{Path.Combine(luaDir, "zapret-auto.lua")}\"";

        var hostlistParam = filterMode switch
        {
            FilterMode.Auto => $" --hostlist-auto=\"{_config.GetPath(ConfigFile.AutoHostlist)}\"",
            FilterMode.Manual => $" --hostlist=\"{_config.GetPath(ConfigFile.Hostlist)}\"",
            _ => string.Empty
        };

        var baseArgs = $"--wf-l3=ipv4 --wf-tcp-out=0-65535 --wf-udp-out=0-65535 --hostlist-exclude=\"{_config.GetPath(ConfigFile.ExcludeList)}\"";

        if (strategy.Contains("--new"))
        {
            var profiles = Regex.Split(strategy, @"\s*--new\s*");
            var fullCmd = $"{baseArgs} {profiles[0].Trim()}{luaParams}{hostlistParam}";

            for (int i = 1; i < profiles.Length; i++)
            {
                var profileStr = profiles[i].Trim();
                if (!profileStr.Contains("--wf-"))
                {
                    fullCmd += $" --new {baseArgs} {profileStr}{luaParams}{hostlistParam}";
                }
                else
                {
                    fullCmd += $" --new --hostlist-exclude=\"{_config.GetPath(ConfigFile.ExcludeList)}\" {profileStr}{luaParams}{hostlistParam}";
                }
            }
            return fullCmd;
        }
        else
        {
            return $"{baseArgs} {strategy.Trim()}{luaParams}{hostlistParam}";
        }
    }

    public async Task<bool> StartAsync(string strategy, FilterMode filterMode)
    {
        await StopAsync();

        await Task.Delay(1500);

        if (Process.GetProcessesByName("winws2").Length > 0)
        {
            throw new Exception(LocalizationService.Get("Msg_ConflictDetected"));
        }

        var args = BuildCommandLine(strategy, filterMode, out var exePath);
        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException($"WinWS dosyası bulunamadı: {exePath}");
        }

        var logPath = @"C:\ProgramData\ZapretDPI-TR\zapret_crash.log";
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

        bool started = false;

        for (int attempt = 0; attempt < 3; attempt++)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = args,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? _config.ZapretWinwsDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            try
            {
                _activeProcess = new Process { StartInfo = psi };

                var lockObj = new object();
                void WriteLog(string? data)
                {
                    if (string.IsNullOrEmpty(data)) return;
                    lock (lockObj)
                    {
                        try { File.AppendAllText(logPath, data + Environment.NewLine); } catch { }
                    }
                }

                _activeProcess.OutputDataReceived += (s, e) => WriteLog(e.Data);
                _activeProcess.ErrorDataReceived += (s, e) => WriteLog(e.Data);

                if (!_activeProcess.Start())
                {
                    _activeProcess = null;
                    if (attempt < 2) { await Task.Delay(2000); continue; }
                    return false;
                }

                _activeProcess.BeginOutputReadLine();
                _activeProcess.BeginErrorReadLine();

                _healthTimer = new System.Threading.Timer(OnHealthCheck, null, 1000, 1000);

                await Task.Delay(800);
                if (_activeProcess.HasExited)
                {
                    _activeProcess = null;
                    _healthTimer?.Dispose();
                    _healthTimer = null;
                    if (attempt < 2) { await Task.Delay(2000); continue; }
                    return false;
                }

                started = true;
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Zapret winws2 başlatılırken hata oluştu");
                _activeProcess = null;
                if (attempt < 2) { await Task.Delay(2000); continue; }
                return false;
            }
            finally
            {
                if (started) StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        return false;
    }

    public Task StopAsync()
    {
        return Task.Run(async () =>
        {
            _healthTimer?.Dispose();
            _healthTimer = null;

            if (_activeProcess != null)
            {
                try
                {
                    if (!_activeProcess.HasExited)
                    {
                        _activeProcess.Kill(true);
                        _activeProcess.WaitForExit(3000);
                    }
                }
                catch { }
                finally
                {
                    _activeProcess?.Dispose();
                    _activeProcess = null;
                }
            }

            foreach (var procName in new[] { "winws2" })
            {
                try
                {
                    foreach (var p in Process.GetProcessesByName(procName))
                    {
                        try
                        {
                            p.Kill(true);
                            p.WaitForExit(2000);
                        }
                        catch { }
                    }
                }
                catch { }
            }

            await Task.Delay(500);
            StateChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private void OnHealthCheck(object? state)
    {
        if (_activeProcess != null && _activeProcess.HasExited)
        {
            _healthTimer?.Dispose();
            _healthTimer = null;
            _activeProcess = null;
            Log.Warning("ZapretProcessManager detected crash.");
            ProcessCrashed?.Invoke(this, EventArgs.Empty);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
