using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.Media.Control;
using WinRT.Interop;
using WinToolbarMediaButtons.Services;

namespace WinToolbarMediaButtons;

public sealed partial class MainWindow : Window
{
    private const int LogicalWidth  = 349;
    private const int LogicalHeight = 52;

    private const string GlyphPlay  = "";
    private const string GlyphPause = "";

    private GlobalSystemMediaTransportControlsSessionManager?    _mediaManager;
    private readonly List<GlobalSystemMediaTransportControlsSession> _trackedSessions = [];
    private DispatcherTimer?        _topmostTimer;
    private AppBarService?          _appBar;
    private WasapiMonitorService?   _wasapi;

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr SetClassLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int  cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;


    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    private const int DWMWA_BORDER_COLOR             = 34;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_COLOR_NONE               = unchecked((int)0xFFFFFFFE);
    private const int DWMWCP_DONOTROUND              = 1;
    private const int GCL_HBRBACKGROUND              = -10;
    private const int GWL_STYLE                      = -16;
    private const int GWL_EXSTYLE                    = -20;
    private const int WS_CHILD                       = 0x40000000;
    private const int WS_POPUP                       = unchecked((int)0x80000000);
    private const int WS_EX_NOREDIRECTIONBITMAP      = 0x00200000;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_NOMOVE     = 0x0002;
    private const uint SWP_NOSIZE     = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static readonly IntPtr HWND_TOP     = IntPtr.Zero;

    public MainWindow()
    {
        InitializeComponent();
        ConfigureWindow();
        ReparentToTaskbar();
        _ = InitMediaMonitorAsync();
        _ = Task.Run(() =>
        {
            try
            {
                var w = new WasapiMonitorService();
                w.StateChanged += RefreshPlayPauseIcon;
                _wasapi = w;
                RefreshPlayPauseIcon();
            }
            catch { }
        });
        var hwnd = WindowNative.GetWindowHandle(this);
        _appBar = new AppBarService(hwnd);
        Closed += (_, _) => { _appBar?.Dispose(); _wasapi?.Dispose(); _topmostTimer?.Stop(); };
        StartTopmostTimer();
    }

    private double GetScale()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        return GetDpiForWindow(hwnd) / 96.0;
    }

    private void ConfigureWindow()
    {
        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
        presenter.IsAlwaysOnTop = true;
        presenter.IsResizable = false;
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;

        AppWindow.TitleBar.ExtendsContentIntoTitleBar    = true;
        AppWindow.TitleBar.BackgroundColor               = Colors.Transparent;
        AppWindow.TitleBar.InactiveBackgroundColor       = Colors.Transparent;
        AppWindow.TitleBar.ButtonBackgroundColor         = Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;


        var hwnd = WindowNative.GetWindowHandle(this);

        int noBorder = DWMWA_COLOR_NONE;
        DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref noBorder, sizeof(int));

        int noRound = DWMWCP_DONOTROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref noRound, sizeof(int));

        SetClassLongPtr(hwnd, GCL_HBRBACKGROUND, IntPtr.Zero);

        var s = GetScale();
        AppWindow.Resize(new SizeInt32((int)(LogicalWidth * s), (int)(LogicalHeight * s)));
    }

    private void ReparentToTaskbar()
    {
        var hwnd        = WindowNative.GetWindowHandle(this);
        var taskbarHwnd = FindWindow("Shell_TrayWnd", null);
        if (taskbarHwnd == IntPtr.Zero) return;

        // WS_CHILD вместо WS_POPUP — окно становится дочерним Shell_TrayWnd
        int style = GetWindowLong(hwnd, GWL_STYLE);
        SetWindowLong(hwnd, GWL_STYLE, (style | WS_CHILD) & ~WS_POPUP);
        SetParent(hwnd, taskbarHwnd);

        // Координаты относительно client area Shell_TrayWnd
        var s = GetScale();
        int w = (int)(LogicalWidth  * s);
        int h = (int)(LogicalHeight * s);
        SetWindowPos(hwnd, HWND_TOP, 0, 0, w, h, SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    private void StartTopmostTimer()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        _topmostTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _topmostTimer.Tick += (_, _) =>
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        _topmostTimer.Start();
    }

    // --- Мониторинг медиа ---

    private async Task InitMediaMonitorAsync()
    {
        _mediaManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _mediaManager.SessionsChanged += (_, _) => UpdateTrackedSessions();
        UpdateTrackedSessions();
    }

    private void UpdateTrackedSessions()
    {
        var current = _mediaManager?.GetSessions()?.ToList() ?? [];

        foreach (var removed in _trackedSessions.Except(current).ToList())
            removed.PlaybackInfoChanged -= OnPlaybackInfoChanged;

        foreach (var added in current.Except(_trackedSessions).ToList())
            added.PlaybackInfoChanged += OnPlaybackInfoChanged;

        _trackedSessions.Clear();
        _trackedSessions.AddRange(current);
        RefreshPlayPauseIcon();
    }

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
        => RefreshPlayPauseIcon();

    private void RefreshPlayPauseIcon()
    {
        var isPlayingSmtc = _trackedSessions.Any(s =>
            s.GetPlaybackInfo()?.PlaybackStatus ==
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);

        var glyph = (isPlayingSmtc || (_wasapi?.IsPlaying ?? false)) ? GlyphPause : GlyphPlay;
        DispatcherQueue.TryEnqueue(() => PlayPauseIcon.Glyph = glyph);
    }

    // --- Обработчики ---

    private void OnRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var exitItem = new MenuFlyoutItem { Text = "Выход" };
        exitItem.Click += (_, _) => ((App)Application.Current).CleanExit();
        var menu = new MenuFlyout();
        menu.Items.Add(exitItem);
        menu.ShowAt((FrameworkElement)sender, e.GetPosition((FrameworkElement)sender));
    }

    private void BtnPrev_Click(object sender, RoutedEventArgs e)      => MediaKeyService.Send(MediaKey.PrevTrack);
    private void BtnPlayPause_Click(object sender, RoutedEventArgs e)  => MediaKeyService.Send(MediaKey.PlayPause);
    private void BtnNext_Click(object sender, RoutedEventArgs e)       => MediaKeyService.Send(MediaKey.NextTrack);
    private void BtnVolDown_Click(object sender, RoutedEventArgs e) => VolumeService.Send(VolumeKey.Down);
    private void BtnVolUp_Click(object sender, RoutedEventArgs e)   => VolumeService.Send(VolumeKey.Up);
    private void BtnMute_Click(object sender, RoutedEventArgs e)    => VolumeService.Send(VolumeKey.Mute);
}
