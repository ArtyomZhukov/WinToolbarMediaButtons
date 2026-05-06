using WinToolbarMediaButtons.Services;

namespace WinToolbarMediaButtons;

// Скрытая форма-хозяйка: message loop + tray icon + lifecycle.
// Всё UI теперь в ToolbarWindow (WS_CHILD Shell_TrayWnd + Windows.UI.Composition).
sealed class MainForm : Form
{
    private NotifyIcon?    _trayIcon;
    private ToolbarWindow? _toolbar;

    public MainForm()
    {
        SuspendLayout();
        FormBorderStyle = FormBorderStyle.None;
        AutoScaleMode   = AutoScaleMode.None;
        ShowInTaskbar   = false;
        StartPosition   = FormStartPosition.Manual;
        Location        = new Point(-32000, -32000);
        Size            = new Size(1, 1);
        ResumeLayout(false);

        InitTrayIcon();

        FormClosed += (_, _) =>
        {
            _toolbar?.Dispose();
            _trayIcon?.Dispose();
        };
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        try
        {
            _toolbar = new ToolbarWindow();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"ToolbarWindow failed:\n{ex}", "WinToolbarMediaButtons",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ── Tray icon ─────────────────────────────────────────────────────────────

    void InitTrayIcon()
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

        try { _trayIcon.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
    }

    static string GetAutostartText() =>
        AutostartService.IsEnabled() ? "Автозапуск: выключить" : "Автозапуск: включить";
}
