using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.Media.Control;
using WinRT.Interop;
using WinToolbarMediaButtons.Services;

namespace WinToolbarMediaButtons;

public sealed partial class MainWindow : Window
{
    private const int LogicalWidth  = 399;
    private const int LogicalHeight = 52;

    private const string GlyphPlay  = "";
    private const string GlyphPause = "";

    private GlobalSystemMediaTransportControlsSessionManager?    _mediaManager;
    private readonly List<GlobalSystemMediaTransportControlsSession> _trackedSessions = [];
    private DispatcherTimer?        _topmostTimer;
    private AppBarService?          _appBar;
    private WasapiMonitorService?   _wasapi;
    private VolumeEndpointService?  _volumeEndpoint;

    private bool  _volumePressActive;
    private bool  _isDragging;
    private float _dragStartX;
    private float _dragStartVolume;

    private readonly SolidColorBrush _volNormal  = new(Windows.UI.Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF));
    private readonly SolidColorBrush _volHover   = new(Windows.UI.Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));

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
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT  { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

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

        try { _volumeEndpoint = new VolumeEndpointService(); } catch { }
        if (_volumeEndpoint is not null)
        {
            var vol = _volumeEndpoint.GetVolume();
            MuteIcon.Glyph         = _volumeEndpoint.GetMute() ? "" : "";
            VolumePercentText.Text = $"{(int)Math.Round(vol * 100)}%";
            UpdateVolumeIcon(vol);
            UpdateVolumeFill(vol);
        }

        Closed += (_, _) => { _appBar?.Dispose(); _wasapi?.Dispose(); _volumeEndpoint?.Dispose(); _topmostTimer?.Stop(); };
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
        var s = GetScale();

        // Flyout — отдельное top-level окно; в WS_CHILD-контексте DPI ему не передаётся
        // автоматически, поэтому масштабируем шрифт вручную через GetScale().
        var exitItem = new MenuFlyoutItem { Text = "Выход", FontSize = 14 * s };
        exitItem.Click += (_, _) => ((App)Application.Current).CleanExit();
        var menu = new MenuFlyout();
        menu.Items.Add(exitItem);

        // e.GetPosition в WS_CHILD-окне считает смещение неверно из-за Shell_TrayWnd parent —
        // берём физические координаты курсора и конвертируем в логические через GetScale().
        GetCursorPos(out var cur);
        GetWindowRect(WindowNative.GetWindowHandle(this), out var wr);
        menu.ShowAt(RootGrid, new FlyoutShowOptions
        {
            Position  = new Windows.Foundation.Point((cur.X - wr.Left) / s, (cur.Y - wr.Top) / s),
            Placement = FlyoutPlacementMode.TopEdgeAlignedLeft,
        });
    }

    private void BtnPrev_Click(object sender, RoutedEventArgs e)     => MediaKeyService.Send(MediaKey.PrevTrack);
    private void BtnPlayPause_Click(object sender, RoutedEventArgs e) => MediaKeyService.Send(MediaKey.PlayPause);
    private void BtnNext_Click(object sender, RoutedEventArgs e)      => MediaKeyService.Send(MediaKey.NextTrack);

    private void BtnMute_Click(object sender, RoutedEventArgs e)
    {
        if (_volumeEndpoint is null) return;
        try
        {
            _volumeEndpoint.ToggleMute();
            MuteIcon.Glyph = _volumeEndpoint.GetMute() ? "" : "";
        }
        catch { }
    }

    private void BtnVolumeControl_PointerEntered(object sender, PointerRoutedEventArgs e)
        => BtnVolumeControl.Background = _volHover;

    private void BtnVolumeControl_PointerExited(object sender, PointerRoutedEventArgs e)
        => BtnVolumeControl.Background = _volNormal;

    private void RootGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // Срабатывает только от BtnVolumeControl — Button-ы помечают PointerPressed как Handled
        // и не дают ему всплыть; Border без обработчика — даёт
        var src = e.OriginalSource as DependencyObject;
        while (src is not null && !ReferenceEquals(src, BtnVolumeControl))
            src = VisualTreeHelper.GetParent(src);
        if (src is null) return;
        if (_volumeEndpoint is null) return;

        _volumePressActive = true;
        _isDragging        = false;
        _dragStartX        = (float)e.GetCurrentPoint(RootGrid).Position.X;
        _dragStartVolume   = _volumeEndpoint.GetVolume();
        RootGrid.CapturePointer(e.Pointer);
    }

    private void RootGrid_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_volumePressActive || _volumeEndpoint is null) return;
        var currentX = (float)e.GetCurrentPoint(RootGrid).Position.X;
        var deltaX   = currentX - _dragStartX;

        if (!_isDragging)
        {
            if (Math.Abs(deltaX) < 4) return;
            _isDragging = true;
            _dragStartX = currentX; // re-anchor: delta starts from 0, не от места нажатия
            return;
        }

        var newVolume = Math.Clamp(_dragStartVolume + deltaX / 200f, 0f, 1f);
        var ep = _volumeEndpoint;
        _ = Task.Run(() => { try { ep.SetVolume(newVolume); } catch { } });
        UpdateSliderDisplay(newVolume);

        // При достижении границы переустанавливаем точку отсчёта — иначе при 0%
        // нужно тащить обратно мимо стартовой позиции, чтобы поднять громкость
        if (newVolume is 0f or 1f)
        {
            _dragStartX      = currentX;
            _dragStartVolume = newVolume;
        }
    }

    private void RootGrid_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_volumePressActive) return;
        _volumePressActive = false;
        RootGrid.ReleasePointerCapture(e.Pointer);
        _isDragging = false;
    }

    private void UpdateSliderDisplay(float volume)
    {
        VolumePercentText.Text = $"{(int)Math.Round(volume * 100)}%";
        UpdateVolumeIcon(volume);
        UpdateVolumeFill(volume);
    }

    private void UpdateVolumeFill(float volume)
    {
        VolumeFillBar.Width = 156.0 * volume;
    }

    private void UpdateVolumeIcon(float volume)
    {
        VolumeIcon.Glyph = volume switch
        {
            0f      => "",
            < 0.33f => "",
            < 0.67f => "",
            _       => "",
        };
    }
}
