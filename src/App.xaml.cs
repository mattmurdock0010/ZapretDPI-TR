using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using ZapretDPI.Services;
using ZapretDPI.ViewModels;

namespace ZapretDPI;

public partial class App : Application
{
    private const string AppGuid = "Global\\92ef468d-b934-4fce-b1bb-f4bc2b4e50eb_ZapretDPI";
    private Mutex? _mutex;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_RESTORE = 9;

    public static IHost? AppHost { get; private set; }

    public App()
    {
        AppHost = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                services.AddSingleton<ConfigService>();
                services.AddSingleton<DnsManager>();
                services.AddSingleton<ZapretProcessManager>();
                services.AddSingleton<WindowsServiceManager>();
                services.AddSingleton<DnscryptManager>();
                services.AddSingleton<BlockcheckRunner>();
                services.AddSingleton<LanShareManager>();
                services.AddSingleton<UpdateService>();

                services.AddSingleton<MainViewModel>();

                services.AddTransient<MainWindow>();
            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        await AppHost!.StartAsync();
        base.OnStartup(e);

        try
        {
            _mutex = new Mutex(true, AppGuid, out var isOnlyInstance);
            if (!isOnlyInstance)
            {
                BringExistingInstanceToFront();
                Shutdown();
                return;
            }
        }
        catch { }

        DispatcherUnhandledException += (s, args) =>
        {
            Log.Error(args.Exception, "Dispatcher Unhandled Exception yakalandı.");
            try
            {
                Views.DarkMessageBox.Show(args.Exception.Message, "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { }
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                Log.Fatal(ex, "AppDomain Unhandled Exception yakalandı.");
            }
        };

        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            Log.Error(args.Exception, "Unobserved Task Exception yakalandı.");
            args.SetObserved();
        };

        var mainWindow = AppHost!.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private static void BringExistingInstanceToFront()
    {
        try
        {
            var current = Process.GetCurrentProcess();
            foreach (var process in Process.GetProcessesByName(current.ProcessName))
            {
                if (process.Id != current.Id && process.MainWindowHandle != IntPtr.Zero)
                {
                    ShowWindow(process.MainWindowHandle, SW_RESTORE);
                    SetForegroundWindow(process.MainWindowHandle);
                    break;
                }
            }
        }
        catch { }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            _mutex?.ReleaseMutex();
        }
        catch { }
        finally
        {
            _mutex?.Dispose();
            _mutex = null;
        }

        await AppHost!.StopAsync();
        AppHost.Dispose();

        base.OnExit(e);
    }
}
