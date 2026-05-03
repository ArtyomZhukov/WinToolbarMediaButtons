using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace WinToolbarMediaButtons;

static class Program
{
    [DllImport("user32.dll")] static extern IntPtr FindWindow(string cls, string? name);
    [DllImport("user32.dll")] static extern uint   GetDpiForWindow(IntPtr hwnd);

    private const int WS_CHILD       = 0x40000000;
    private const int WS_VISIBLE     = 0x10000000;
    private const int WS_CLIPCHILDREN = 0x02000000;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [STAThread]
    static void Main()
    {
        var taskbar = FindWindow("Shell_TrayWnd", null);
        var scale   = Math.Max(1f, GetDpiForWindow(taskbar) / 96f);

        var p = new HwndSourceParameters("MediaButtons")
        {
            WindowStyle         = WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN,
            ExtendedWindowStyle = WS_EX_NOACTIVATE,
            ParentWindow        = taskbar,
            PositionX           = 0,
            PositionY           = 0,
            Width               = (int)(399 * scale),
            Height              = (int)(52  * scale),
        };

        var source = new HwndSource(p)
        {
            RootVisual = new System.Windows.Controls.Grid
            {
                Background = System.Windows.Media.Brushes.Red, // DEBUG
            }
        };

        new System.Windows.Application().Run();
        GC.KeepAlive(source);
    }
}
