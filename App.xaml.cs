using Microsoft.UI.Xaml;
using WinRT.Interop;
using WinToolbarMediaButtons.Services;

namespace WinToolbarMediaButtons;

public partial class App : Application
{
    private MainWindow?      _window;
    private TrayIconService? _tray;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            try
            {
                var log = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    "WinToolbarCrash.log");
                File.AppendAllText(log,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}]\n" +
                    $"{e.Exception?.GetType()?.FullName}: {e.Exception?.Message}\n" +
                    $"{e.Exception?.StackTrace}\n\n");
            }
            catch { }
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();

        var hwnd     = WindowNative.GetWindowHandle(_window);
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        _tray = new TrayIconService(
            hwnd, iconPath,
            getAutostart:    AutostartService.IsEnabled,
            toggleAutostart: () => { if (AutostartService.IsEnabled()) AutostartService.Disable(); else AutostartService.Enable(); },
            exit:            CleanExit);
    }

    public void CleanExit()
    {
        _tray?.Dispose();
        Environment.Exit(0);
    }
}
