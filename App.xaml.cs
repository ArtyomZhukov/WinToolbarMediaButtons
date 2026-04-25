using System.Drawing;
using H.NotifyIcon;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinToolbarMediaButtons.Services;

namespace WinToolbarMediaButtons;

public partial class App : Application
{
    private MainWindow? _window;
    private TaskbarIcon? _trayIcon;

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
        InitTrayIcon();
    }

    private void InitTrayIcon()
    {
        var autostartItem = new MenuFlyoutItem { Text = GetAutostartText() };
        autostartItem.Click += (_, _) =>
        {
            if (AutostartService.IsEnabled()) AutostartService.Disable();
            else AutostartService.Enable();
            autostartItem.Text = GetAutostartText();
        };

        var exitItem = new MenuFlyoutItem { Text = "Выход" };
        exitItem.Click += (_, _) => CleanExit();

        var menu = new MenuFlyout();
        menu.Items.Add(autostartItem);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(exitItem);

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "Media Buttons",
            ContextFlyout = menu,
            Icon = new Icon(iconPath),
        };
        _trayIcon.ForceCreate();
    }

    public void CleanExit()
    {
        _trayIcon?.Dispose();
        Environment.Exit(0);
    }

    private static string GetAutostartText() =>
        AutostartService.IsEnabled() ? "Автозапуск: ✓ выключить" : "Автозапуск: включить";
}
