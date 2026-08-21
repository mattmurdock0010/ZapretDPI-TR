using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Serilog;
using ZapretDPI.Models;

namespace ZapretDPI.Services;

public class WindowsServiceManager
{
    private const string ServiceName = "ZapretService";
    private readonly ZapretProcessManager _processManager;
    private readonly ConfigService _config;
    private Process? _serviceProcess;

    public event EventHandler? StateChanged;

    public WindowsServiceManager(ZapretProcessManager processManager, ConfigService config)
    {
        _processManager = processManager;
        _config = config;
    }

    private void NotifyStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    public async Task<bool> IsServiceRunningAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                if (_serviceProcess != null && !_serviceProcess.HasExited)
                    return true;

                var procs = Process.GetProcessesByName("winws2");
                if (procs.Length > 0 && !_processManager.IsRunning)
                {
                    return true;
                }
            }
            catch { }

            return false;
        });
    }

    public async Task<bool> IsServiceInstalledAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                var output = RunCmdWithOutput($"sc.exe query {ServiceName}");
                return output.Contains(ServiceName, StringComparison.OrdinalIgnoreCase) && !output.Contains("1060");
            }
            catch { }
            return false;
        });
    }

    /// <summary>
    /// Checks if the scheduled task is registered (even if the process isn't running yet).
    /// </summary>
    public bool IsServiceInstalled()
    {
        try
        {
            var taskPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "Tasks", ServiceName);
            return File.Exists(taskPath);
        }
        catch { }
        return false;
    }

    public async Task<bool> InstallServiceAsync(string strategy, FilterMode filterMode)
    {
        try
        {
            await RemoveServiceAsync();
            await Task.Delay(600);

            return await Task.Run(async () =>
            {
                if (Process.GetProcessesByName("winws2").Length > 0)
                {
                    throw new Exception(LocalizationService.Get("Msg_ConflictDetected"));
                }

                try
                {
                    var args = _processManager.BuildCommandLine(strategy, filterMode, out var exePath);

                    if (!File.Exists(exePath))
                    {
                        Log.Error("WinWS not found at {ExePath}", exePath);
                        return false;
                    }

                    var workDir = Path.GetDirectoryName(exePath) ?? _config.ZapretWinwsDir;
                    var baseZapretDir = Path.GetDirectoryName(workDir);
                    var winswSource = Path.Combine(baseZapretDir!, "tools", "winsw.exe");

                    var serviceExePath = Path.Combine(workDir, "zapret-service.exe");
                    var serviceXmlPath = Path.Combine(workDir, "zapret-service.xml");

                    if (!File.Exists(winswSource))
                    {
                        Log.Error("WinSW not found at {Source}", winswSource);
                        return false;
                    }

                    File.Copy(winswSource, serviceExePath, true);

                    var xmlContent = $@"<service>
  <id>{ServiceName}</id>
  <name>Zapret DPI Bypass</name>
  <description>Zapret DPI Bypass (winws2) Service</description>
  <executable>{exePath}</executable>
  <arguments>{args}</arguments>
  <workingdirectory>{workDir}</workingdirectory>
  <log mode=""roll""></log>
  <onfailure action=""restart"" delay=""5 sec""/>
  <onfailure action=""restart"" delay=""10 sec""/>
</service>";

                    File.WriteAllText(serviceXmlPath, xmlContent);

                    // Install the service via WinSW
                    var installCmd = $"\"{serviceExePath}\" install";
                    var installResult = RunCmdWithOutput(installCmd);
                    Log.Information("WinSW Install output: {Output}", installResult);

                    // Start the service
                    var startCmd = $"\"{serviceExePath}\" start";
                    RunCmd(startCmd);
                    Log.Information("Service '{ServiceName}' start command sent.", ServiceName);

                    bool started = false;
                    for (int i = 0; i < 10; i++)
                    {
                        await Task.Delay(500);
                        if (await IsServiceRunningAsync())
                        {
                            started = true;
                            break;
                        }
                    }

                    if (!started)
                    {
                        Log.Warning("Scheduled task didn't start winws2, trying direct process fallback...");

                        var psi = new ProcessStartInfo
                        {
                            FileName = exePath,
                            Arguments = args,
                            WorkingDirectory = workDir,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            WindowStyle = ProcessWindowStyle.Hidden,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        };

                        _serviceProcess = new Process { StartInfo = psi };

                        _serviceProcess.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) Log.Information("[winws2] {Data}", e.Data); };
                        _serviceProcess.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) Log.Error("[winws2 ERROR] {Data}", e.Data); };

                        if (_serviceProcess.Start())
                        {
                            _serviceProcess.BeginOutputReadLine();
                            _serviceProcess.BeginErrorReadLine();
                        }

                        for (int i = 0; i < 4; i++)
                        {
                            await Task.Delay(500);
                            if (await IsServiceRunningAsync()) return true;
                        }
                    }

                    return await IsServiceRunningAsync();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error in Install (Task)");
                    return false;
                }
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Outer Install error");
            return false;
        }
        finally
        {
            NotifyStateChanged();
        }
    }

    public async Task RemoveServiceAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                var serviceExePath = Path.Combine(_config.ZapretWinwsDir, "zapret-service.exe");
                if (File.Exists(serviceExePath))
                {
                    RunCmd($"\"{serviceExePath}\" stop");
                    Task.Delay(1000).Wait();
                    RunCmd($"\"{serviceExePath}\" uninstall");
                }

                // Fallback / old schtasks cleanup
                RunCmd($"schtasks.exe /end /tn \"{ServiceName}\"");
                RunCmd($"schtasks.exe /delete /tn \"{ServiceName}\" /f");
                RunSc("stop", ServiceName);
                RunSc("delete", ServiceName);

                if (_serviceProcess != null)
                {
                    try
                    {
                        if (!_serviceProcess.HasExited)
                        {
                            _serviceProcess.Kill(true);
                            _serviceProcess.WaitForExit(1000);
                        }
                    }
                    catch { }
                    finally
                    {
                        _serviceProcess?.Dispose();
                        _serviceProcess = null;
                    }
                }

                RunCmd("sc.exe stop WinDivert");
                RunCmd("sc.exe stop WinDivert14");
                RunCmd("sc.exe stop monkey");

                RunCmd("taskkill.exe /F /IM winws2.exe /T");

                foreach (var procName in new[] { "winws2" })
                {
                    try
                    {
                        foreach (var p in Process.GetProcessesByName(procName))
                        {
                            try { p.Kill(true); p.WaitForExit(500); } catch { }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        });
        NotifyStateChanged();
    }

    private static string RunSc(string action, string target)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"{action} {target}",
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

    private static void RunCmd(string command)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{command}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(3000);
        }
        catch { }
    }

    private static string RunCmdWithOutput(string command)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{command}\"",
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
                p.WaitForExit(5000);
                return output;
            }
        }
        catch { }
        return string.Empty;
    }
}
