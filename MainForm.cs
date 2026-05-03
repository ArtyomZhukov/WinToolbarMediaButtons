using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using WinToolbarMediaButtons.Services;

namespace WinToolbarMediaButtons;

sealed class MainForm : Form
{
    // Layout in logical pixels (matches original XAML exactly)
    private const int LogicalWidth  = 399;
    private const int LogicalHeight = 52;

    private static readonly Rectangle RegPrev   = new(4,   0, 52,  52);
    private static readonly Rectangle RegPlay   = new(58,  0, 52,  52);
    private static readonly Rectangle RegNext   = new(112, 0, 52,  52);
    private static readonly Rectangle RegMute   = new(189, 0, 52,  52);
    private static readonly Rectangle RegVolume = new(243, 0, 156, 52);

    private const string GlyphPrev  = "";
    private const string GlyphPlay  = "";
    private const string GlyphPause = "";
    private const string GlyphNext  = "";

    // UI state
    private string _playGlyph = GlyphPlay;
    private string _muteGlyph = "";
    private string _volGlyph  = "";
    private float  _volume    = 1f;
    private string _volPct    = "100%";

    private Rectangle? _hoveredBtn;
    private Rectangle? _pressedBtn;
    private bool       _volHovered;

    // Volume drag
    private bool  _dragActive;
    private bool  _isDragging;
    private float _dragStartX;
    private float _dragStartVolume;

    // Services
    private WasapiMonitorService?  _wasapi;
    private VolumeEndpointService? _volumeEndpoint;

    private System.Windows.Forms.Timer? _topmostTimer;
    private NotifyIcon?                 _trayIcon;
    private float                       _scale        = 1f;
    private bool                        _embedded;
    private bool                        _usingBand;

    // ── P/Invoke ──────────────────────────────────────────────────────────────

    [DllImport("user32.dll")] static extern uint   GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool   SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);
    [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int    GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] static extern bool   GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll", EntryPoint = "SetWindowBand", SetLastError = true)]
    static extern bool SetWindowBand(IntPtr hwnd, IntPtr hwndInsertAfter, uint dwBand);
    [DllImport("dwmapi.dll")] static extern int    DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private const int  GWL_STYLE    = -16;
    private const int  WS_VISIBLE   = 0x10000000;
    private const int  WS_CHILD     = 0x40000000;
    private const int  WS_POPUP     = unchecked((int)0x80000000);
    private const int  DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int  DWMWCP_DONOTROUND              = 1;
    private const uint SWP_NOMOVE     = 0x0002;
    private const uint SWP_NOSIZE     = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const int  WS_EX_NOACTIVATE    = 0x08000000;
    private const int  WS_EX_TOOLWINDOW    = 0x00000080;
    private const uint ZBID_IMMERSIVE_MOGO = 5;          // taskbar z-band
    private static readonly IntPtr HWND_TOP     = IntPtr.Zero;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    // ── Window style ──────────────────────────────────────────────────────────

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
    {
        if (_embedded) return;
        base.SetBoundsCore(x, y, width, height, specified);
    }

    // ── Constructor ───────────────────────────────────────────────────────────

    public MainForm()
    {
        SuspendLayout();
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar   = false;
        BackColor       = Color.FromArgb(0x1C, 0x1C, 0x1C);
        DoubleBuffered  = true;
        ResumeLayout(false);

        InitTrayIcon();

        _ = Task.Run(() =>
        {
            try
            {
                var w = new WasapiMonitorService();
                w.StateChanged += RefreshPlayPause;
                _wasapi = w;
                RefreshPlayPause();
            }
            catch { }
        });

        try
        {
            _volumeEndpoint = new VolumeEndpointService();
            var vol = _volumeEndpoint.GetVolume();
            _muteGlyph = _volumeEndpoint.GetMute() ? "" : "";
            _volume    = vol;
            _volPct    = $"{(int)Math.Round(vol * 100)}%";
            _volGlyph  = VolumeGlyph(vol);
        }
        catch { }

        FormClosed += (_, _) =>
        {
            _wasapi?.Dispose();
            _volumeEndpoint?.Dispose();
            _topmostTimer?.Stop();
            _trayIcon?.Dispose();
        };
    }

    // ── Window setup ─────────────────────────────────────────────────────────

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _scale = GetDpiForWindow(Handle) / 96f;
        if (_scale <= 0f) _scale = 1f;
        int w = (int)(LogicalWidth  * _scale);
        int h = (int)(LogicalHeight * _scale);

        int noRound = DWMWCP_DONOTROUND;
        DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref noRound, sizeof(int));

        GetWindowRect(FindWindow("Shell_TrayWnd", null), out var taskbar);
        SetWindowPos(Handle, HWND_TOPMOST, taskbar.Left, taskbar.Top, w, h, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        try { _usingBand = SetWindowBand(Handle, IntPtr.Zero, ZBID_IMMERSIVE_MOGO); } catch { _usingBand = false; }
        _embedded = true;

        StartTopmostTimer();
    }

    private void StartTopmostTimer()
    {
        _topmostTimer = new System.Windows.Forms.Timer { Interval = 100 };
        _topmostTimer.Tick += (_, _) =>
        {
            if (_usingBand)
                try { SetWindowBand(Handle, IntPtr.Zero, ZBID_IMMERSIVE_MOGO); } catch { }
            else
                SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        };
        _topmostTimer.Start();
    }

    // ── Drawing ───────────────────────────────────────────────────────────────

    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode      = SmoothingMode.AntiAlias;
        g.TextRenderingHint  = System.Drawing.Text.TextRenderingHint.AntiAlias;
        float s = _scale;

        g.Clear(Color.FromArgb(0x1C, 0x1C, 0x1C));

        DrawBtn(g, RegPrev, GlyphPrev,  s);
        DrawBtn(g, RegPlay, _playGlyph, s);
        DrawBtn(g, RegNext, GlyphNext,  s);

        // Separator: logical x=176, y=[10..42]
        using (var pen = new Pen(Color.FromArgb(107, 255, 255, 255), Math.Max(1f, s)))
            g.DrawLine(pen, 176 * s, 10 * s, 176 * s, 42 * s);

        DrawBtn(g, RegMute, _muteGlyph, s);
        DrawVolumeControl(g, s);
    }

    private void DrawBtn(Graphics g, Rectangle lr, string glyph, float s)
    {
        var pr = ScaleRect(lr, s);

        if (_pressedBtn == lr)
        {
            using var b = new SolidBrush(Color.FromArgb(0x11, 0xFF, 0xFF, 0xFF));
            g.FillRectangle(b, pr);
        }
        else if (_hoveredBtn == lr)
        {
            using var b = new SolidBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
            g.FillRectangle(b, pr);
        }

        DrawGlyph(g, glyph, 22 * s, pr);
    }

    private void DrawVolumeControl(Graphics g, float s)
    {
        var pr     = ScaleRect(RegVolume, s);
        int radius = (int)(4 * s);

        using var clipPath = RoundedPath(pr, radius);

        using (var b = new SolidBrush(Color.FromArgb(_volHovered ? 0x30 : 0x15, 0xFF, 0xFF, 0xFF)))
            g.FillPath(b, clipPath);

        if (_volume > 0f)
        {
            var state = g.Save();
            g.SetClip(clipPath);
            using (var b = new SolidBrush(Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF)))
                g.FillRectangle(b, pr.X, pr.Y, pr.Width * _volume, pr.Height);
            g.Restore(state);
        }

        // Measure and center the icon+text pair
        using var iconFont = new Font("Segoe Fluent Icons", 22 * s, GraphicsUnit.Pixel);
        using var txtFont  = new Font("Segoe UI", 16 * s, FontStyle.Bold, GraphicsUnit.Pixel);
        using var white    = new SolidBrush(Color.White);

        var iconSz = g.MeasureString(_volGlyph, iconFont);
        var txtSz  = g.MeasureString(_volPct,   txtFont);
        float gap    = 8 * s;
        float totalW = iconSz.Width + gap + txtSz.Width;
        float startX = pr.X + (pr.Width - totalW) / 2f;
        float midY   = pr.Y + pr.Height / 2f;

        g.DrawString(_volGlyph, iconFont, white, startX,                           midY - iconSz.Height / 2f);
        g.DrawString(_volPct,   txtFont,  white, startX + iconSz.Width + gap,      midY - txtSz.Height  / 2f);
    }

    private static void DrawGlyph(Graphics g, string glyph, float size, RectangleF bounds)
    {
        using var font  = new Font("Segoe Fluent Icons", size, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(Color.White);
        var sz = g.MeasureString(glyph, font);
        float x = bounds.X + (bounds.Width  - sz.Width)  / 2f;
        float y = bounds.Y + (bounds.Height - sz.Height) / 2f;
        g.DrawString(glyph, font, brush, x, y);
    }

    private static GraphicsPath RoundedPath(RectangleF r, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(r.X,           r.Y,            d, d, 180, 90);
        path.AddArc(r.Right - d,   r.Y,            d, d, 270, 90);
        path.AddArc(r.Right - d,   r.Bottom - d,   d, d, 0,   90);
        path.AddArc(r.X,           r.Bottom - d,   d, d, 90,  90);
        path.CloseFigure();
        return path;
    }

    private static RectangleF ScaleRect(Rectangle r, float s)
        => new(r.X * s, r.Y * s, r.Width * s, r.Height * s);

    // ── Mouse ─────────────────────────────────────────────────────────────────

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var lp = Unscale(e.Location);

        if (_dragActive)
        {
            HandleDrag(lp.X);
            return;
        }

        var newHov  = HitTest(lp);
        var newVolH = RegVolume.Contains(lp);
        if (newHov != _hoveredBtn || newVolH != _volHovered)
        {
            _hoveredBtn = newHov;
            _volHovered = newVolH;
            Invalidate();
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        var lp = Unscale(e.Location);

        if (e.Button == MouseButtons.Right)
        {
            ShowContextMenu();
            return;
        }

        if (e.Button != MouseButtons.Left) return;

        if (RegVolume.Contains(lp) && _volumeEndpoint is not null)
        {
            _dragActive      = true;
            _isDragging      = false;
            _dragStartX      = lp.X;
            _dragStartVolume = _volumeEndpoint.GetVolume();
            Capture          = true;
        }
        else
        {
            _pressedBtn = HitTest(lp);
            if (_pressedBtn.HasValue) Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;

        if (_dragActive)
        {
            _dragActive = false;
            _isDragging = false;
            Capture     = false;
            return;
        }

        if (_pressedBtn.HasValue)
        {
            var lp = Unscale(e.Location);
            if (_pressedBtn.Value.Contains(lp))
                FireButton(_pressedBtn.Value);
            _pressedBtn = null;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoveredBtn.HasValue || _volHovered)
        {
            _hoveredBtn = null;
            _volHovered = false;
            Invalidate();
        }
    }

    private void HandleDrag(float lpX)
    {
        if (!_isDragging)
        {
            if (Math.Abs(lpX - _dragStartX) < 4) return;
            _isDragging = true;
            _dragStartX = lpX;
            return;
        }

        float deltaX = lpX - _dragStartX;
        float newVol = Math.Clamp(_dragStartVolume + deltaX / 200f, 0f, 1f);
        var ep = _volumeEndpoint;
        if (ep is not null)
            _ = Task.Run(() => { try { ep.SetVolume(newVol); } catch { } });
        UpdateVolumeState(newVol);

        if (newVol is 0f or 1f)
        {
            _dragStartX      = lpX;
            _dragStartVolume = newVol;
        }
    }

    private void FireButton(Rectangle region)
    {
        if      (region == RegPrev) MediaKeyService.Send(MediaKey.PrevTrack);
        else if (region == RegPlay) MediaKeyService.Send(MediaKey.PlayPause);
        else if (region == RegNext) MediaKeyService.Send(MediaKey.NextTrack);
        else if (region == RegMute) ToggleMute();
    }

    private void ToggleMute()
    {
        if (_volumeEndpoint is null) return;
        try
        {
            _volumeEndpoint.ToggleMute();
            _muteGlyph = _volumeEndpoint.GetMute() ? "" : "";
            Invalidate();
        }
        catch { }
    }

    private static Rectangle? HitTest(Point lp)
    {
        if (RegPrev.Contains(lp)) return RegPrev;
        if (RegPlay.Contains(lp)) return RegPlay;
        if (RegNext.Contains(lp)) return RegNext;
        if (RegMute.Contains(lp)) return RegMute;
        return null;
    }

    private Point Unscale(Point physical)
        => new((int)(physical.X / _scale), (int)(physical.Y / _scale));

    // ── Context menu ──────────────────────────────────────────────────────────

    private void ShowContextMenu()
    {
        var autostartItem = new ToolStripMenuItem(GetAutostartText());
        autostartItem.Click += (_, _) =>
        {
            if (AutostartService.IsEnabled()) AutostartService.Disable();
            else AutostartService.Enable();
        };

        var exitItem = new ToolStripMenuItem("Выход");
        exitItem.Click += (_, _) => Application.Exit();

        using var menu = new ContextMenuStrip();
        menu.Items.Add(autostartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);
        menu.Show(Cursor.Position);
    }

    // ── Tray icon ─────────────────────────────────────────────────────────────

    private void InitTrayIcon()
    {
        var autostartItem = new ToolStripMenuItem(GetAutostartText());
        autostartItem.Click += (_, _) =>
        {
            if (AutostartService.IsEnabled()) AutostartService.Disable();
            else AutostartService.Enable();
            autostartItem.Text = GetAutostartText();
        };

        var exitItem = new ToolStripMenuItem("Выход");
        exitItem.Click += (_, _) => Application.Exit();

        var menu = new ContextMenuStrip();
        menu.Items.Add(autostartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        _trayIcon = new NotifyIcon
        {
            Text             = "Media Buttons",
            ContextMenuStrip = menu,
            Visible          = true,
        };

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        if (File.Exists(iconPath))
            _trayIcon.Icon = new System.Drawing.Icon(iconPath);
    }

    private static string GetAutostartText() =>
        AutostartService.IsEnabled() ? "Автозапуск: ✓ выключить" : "Автозапуск: включить";

    // ── Play/Pause state (WASAPI-only) ────────────────────────────────────────

    private void RefreshPlayPause()
    {
        var glyph = (_wasapi?.IsPlaying ?? false) ? GlyphPause : GlyphPlay;
        if (_playGlyph == glyph) return;
        _playGlyph = glyph;
        if (IsHandleCreated) BeginInvoke(() => Invalidate());
    }

    // ── Volume helpers ────────────────────────────────────────────────────────

    private void UpdateVolumeState(float volume)
    {
        _volume   = volume;
        _volPct   = $"{(int)Math.Round(volume * 100)}%";
        _volGlyph = VolumeGlyph(volume);
        if (IsHandleCreated) BeginInvoke(() => Invalidate());
    }

    // ── Debug dialog ──────────────────────────────────────────────────────────

    private static void ShowDebugDialog(string title, string text)
    {
        var dlg = new Form
        {
            Text            = $"DEBUG — {title}",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition   = FormStartPosition.CenterScreen,
            MinimizeBox     = false,
            MaximizeBox     = false,
            Width           = 420,
            Height          = 200,
            TopMost         = true,
        };

        var tb = new TextBox
        {
            Multiline  = true,
            ReadOnly   = true,
            ScrollBars = ScrollBars.Vertical,
            Text       = text,
            Dock       = DockStyle.Fill,
            Font       = new Font("Consolas", 10f),
        };

        var btnCopy = new Button { Text = "Копировать", Dock = DockStyle.Bottom, Height = 32 };
        btnCopy.Click += (_, _) => Clipboard.SetText(text);

        var btnClose = new Button { Text = "Закрыть", Dock = DockStyle.Bottom, Height = 32 };
        btnClose.Click += (_, _) => dlg.Close();

        dlg.Controls.Add(tb);
        dlg.Controls.Add(btnCopy);
        dlg.Controls.Add(btnClose);
        dlg.ShowDialog();
    }

    private static string VolumeGlyph(float v) => v switch
    {
        0f      => "",
        < 0.33f => "",
        < 0.67f => "",
        _       => "",
    };
}
