using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using Serilog;
using Velopack;

namespace ZapretDPI;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ZapretDPI-TR", "logs");
        Directory.CreateDirectory(logDir);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .WriteTo.File(Path.Combine(logDir, "zapret-tr-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
            .CreateLogger();

        try
        {
            Log.Information("ZapretDPI-TR başlatılıyor...");
            VelopackApp.Build().Run();
        }
        catch { }

        if (!IsAdministrator())
        {
            Log.Warning("Uygulama yönetici yetkisi olmadan başlatıldı, yeniden başlatılıyor (runas)...");
            try
            {
                var currentExe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(currentExe))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = currentExe,
                        UseShellExecute = true,
                        Verb = "runas"
                    };
                    Process.Start(psi);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Yönetici izni istenirken hata oluştu.");
            }
            finally
            {
                Log.CloseAndFlush();
            }
            return;
        }

        try
        {
            var app = new App();
            app.InitializeComponent();
            app.Run();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Uygulama çöktü (Unhandled Exception).");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
