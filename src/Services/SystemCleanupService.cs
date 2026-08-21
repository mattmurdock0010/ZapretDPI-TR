using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Serilog;

namespace ZapretDPI.Services;

public static class SystemCleanupService
{
    public static void StopAllServicesAndProcessesSilently()
    {
        try
        {
            RunSc("stop ZapretService");
            RunSc("delete ZapretService");

            RunSc("stop dnscrypt-proxy");
            RunSc("delete dnscrypt-proxy");

            RunSc("stop WinDivert");
            RunSc("stop WinDivert14");
            RunSc("stop monkey");

            KillProcesses("winws2", "dnscrypt-proxy", "go-pcap2socks");

            RunCmd("netsh interface ip set dns name=\"*\" source=dhcp");
            RunCmd("netsh interface ip set dns name=\"*\" source=dhcp");
            RunCmd("ipconfig /flushdns");

            Log.Information("Sistem servisleri ve süreçleri sessizce temizlendi.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Sistem servisleri temizlenirken hata oluştu.");
        }
    }

    public static async Task StopAllServicesAndProcessesAsync()
    {
        Log.Information("Kapsamlı temizlik başlatılıyor (StopAllServicesAndProcessesAsync)...");
        await Task.Run(StopAllServicesAndProcessesSilently);
    }

    public static async Task DeepCleanupAsync()
    {
        Log.Information("Derinlemesine Çatışma Çözücü başlatılıyor (DeepCleanupAsync)...");
        await Task.Run(() =>
        {
            try
            {
                // Bilinen tüm rakip/eski araçların servisleri
                RunSc("stop GoodbyeDPI");
                RunSc("delete GoodbyeDPI");

                RunSc("stop spoof-dpi");
                RunSc("delete spoof-dpi");

                RunSc("stop green-tunnel");
                RunSc("delete green-tunnel");

                // Kalan WinDivert filtrelerini temizle (Driver unload)
                RunSc("stop WinDivert");
                RunSc("delete WinDivert");
                RunSc("stop WinDivert14");
                RunSc("delete WinDivert14");

                // Standart Zapret temizliği
                StopAllServicesAndProcessesSilently();

                // Tüm ağ bağdaştırıcılarında DNS'i otomatiğe çek ve önbelleği temizle
                RunCmd("netsh interface ip set dns name=\"*\" source=dhcp");
                RunCmd("netsh interface ipv6 set dns name=\"*\" source=dhcp");
                RunCmd("ipconfig /flushdns");

                Log.Information("Derinlemesine temizlik başarıyla tamamlandı.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Derinlemesine temizlik sırasında hata oluştu.");
            }
        });
    }

    private static void KillProcesses(params string[] processNames)
    {
        foreach (var name in processNames)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "taskkill.exe",
                    Arguments = $"/F /IM {name}.exe /T",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(1000);
            }
            catch { }

            try
            {
                foreach (var proc in Process.GetProcessesByName(name))
                {
                    try
                    {
                        proc.Kill(true);
                        proc.WaitForExit(500);
                    }
                    catch { }
                    finally
                    {
                        proc.Dispose();
                    }
                }
            }
            catch { }
        }
    }

    private static void RunSc(string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(1500);
        }
        catch { }
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
            p?.WaitForExit(2000);
        }
        catch { }
    }
}
