using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ZapretDPI.Models;

namespace ZapretDPI.Services;

public class BlockcheckRunner
{
    private readonly ConfigService _config;

    public BlockcheckRunner(ConfigService config)
    {
        _config = config;
    }

    public async Task<List<string>> RunBlockcheckAsync(ScanMode mode, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        return await Task.Run(async () =>
        {
            var logPath = _config.Blockcheck2Log;

            if (File.Exists(logPath))
            {
                try { File.Delete(logPath); } catch { }
            }

            var bashPath = _config.CygwinBashPath;
            var shScriptPath = Path.Combine(_config.BlockcheckDir, "zapret2", "blog.sh");

            var posixScript = shScriptPath.Replace('\\', '/');

            var psi = new ProcessStartInfo
            {
                FileName = bashPath,
                Arguments = $"\"{posixScript}\"",
                WorkingDirectory = _config.BlockcheckDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            var outputSb = new StringBuilder();
            var lockObj = new object();
            var foundStrategies = new List<string>();

            process.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    lock (lockObj)
                    {
                        outputSb.AppendLine(e.Data);
                    }
                    progress?.Report(e.Data);
                }
            };

            process.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    lock (lockObj)
                    {
                        outputSb.AppendLine(e.Data);
                    }
                    progress?.Report(e.Data);
                }
            };

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                progress?.Report($"[Hata] Cygwin başlatılamadı: {ex.Message}");
                return new List<string>();
            }

            progress?.Report("Analiz başlatıldı, DPI bypass paketleri test ediliyor...");

            var startTime = DateTime.UtcNow;

            try
            {
                while (!process.HasExited && (DateTime.UtcNow - startTime).TotalMinutes < 12)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        try { process.Kill(true); } catch { }
                        CleanupProcesses();
                        return new List<string>();
                    }

                    await Task.Delay(250, cancellationToken);

                    string currentOutput;
                    lock (lockObj)
                    {
                        currentOutput = outputSb.ToString();
                    }

                    var filtered = currentOutput.Replace("iana.org", "IGNORE");
                    foundStrategies = ExtractZapret2Strategies(filtered);

                    if (mode == ScanMode.Fast && foundStrategies.Count >= 1)
                    {
                        try { process.Kill(true); } catch { }
                        break;
                    }
                    if (mode == ScanMode.Smart && foundStrategies.Count >= 10)
                    {
                        try { process.Kill(true); } catch { }
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(true); } catch { }
                CleanupProcesses();
                return new List<string>();
            }

            try
            {
                if (!process.HasExited)
                {
                    process.WaitForExit(1000);
                }
            }
            catch { }

            CleanupProcesses();

            try
            {
                lock (lockObj)
                {
                    File.WriteAllText(logPath, outputSb.ToString());
                }
            }
            catch { }

            string finalOutput;
            lock (lockObj)
            {
                finalOutput = outputSb.ToString();
            }

            finalOutput = finalOutput.Replace("iana.org", "IGNORE");
            var finalStrategies = ExtractZapret2Strategies(finalOutput);
            foreach (var s in finalStrategies)
            {
                if (!foundStrategies.Contains(s)) foundStrategies.Add(s);
            }

            return foundStrategies;
        }, cancellationToken);
    }

    private static List<string> ExtractZapret2Strategies(string logContent)
    {
        var strategies = new List<string>();
        var lines = logContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains("iana.org", StringComparison.OrdinalIgnoreCase)) continue;
            if (lines[i].Contains("!!!!! AVAILABLE !!!!!"))
            {
                if (i > 0)
                {
                    var prevLine = lines[i - 1];
                    const string targetStr = "--wf-tcp-out=443";
                    var startPos = prevLine.IndexOf(targetStr, StringComparison.OrdinalIgnoreCase);
                    if (startPos >= 0)
                    {
                        var raw = prevLine[(startPos + targetStr.Length)..].Trim();
                        var formatted = FormatStrategyQuotes(raw);
                        if (!strategies.Contains(formatted))
                            strategies.Add(formatted);
                    }
                }
            }
        }
        return strategies;
    }

    public static string FormatStrategyQuotes(string rawStrategy)
    {
        var tokens = rawStrategy.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var formatted = tokens.Select(token =>
        {
            var eq = token.IndexOf('=');
            if (eq > 0 && !token.EndsWith("\""))
            {
                var key = token[..(eq + 1)];
                var val = token[(eq + 1)..].Trim('"');
                return $"{key}\"{val}\"";
            }
            return token;
        });

        return string.Join(" ", formatted);
    }

    private static void CleanupProcesses()
    {
        foreach (var name in new[] { "bash", "sh", "tee", "winws2" })
        {
            try
            {
                foreach (var p in Process.GetProcessesByName(name))
                {
                    try { p.Kill(true); } catch { }
                }
            }
            catch { }
        }
    }
}
