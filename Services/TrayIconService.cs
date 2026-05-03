using System.Runtime.InteropServices;

namespace WinToolbarMediaButtons.Services;

sealed class TrayIconService : IDisposable
{
    [DllImport("shell32.dll")]
    static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpdata);

    [DllImport("user32.dll")]
    static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool AppendMenu(IntPtr hMenu, uint uFlags, nuint uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    static extern int TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll")]
    static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out POINT pt);

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("comctl32.dll")]
    static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, nuint uIdSubclass, nuint dwRefData);

    [DllImport("comctl32.dll")]
    static extern bool RemoveWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, nuint uIdSubclass);

    [DllImport("comctl32.dll")]
    static extern nint DefSubclassProc(IntPtr hWnd, uint uMsg, nuint wParam, nint lParam);

    delegate nint SUBCLASSPROC(IntPtr hWnd, uint uMsg, nuint wParam, nint lParam, nuint uIdSubclass, nuint dwRefData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NOTIFYICONDATA
    {
        public uint   cbSize;
        public IntPtr hWnd;
        public uint   uID;
        public uint   uFlags;
        public uint   uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint   dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint   uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]  public string szInfoTitle;
        public uint   dwInfoFlags;
        public Guid   guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X, Y; }

    const uint NIM_ADD            = 0;
    const uint NIM_DELETE         = 2;
    const uint NIM_SETVERSION     = 4;
    const uint NIF_MESSAGE        = 0x01;
    const uint NIF_ICON           = 0x02;
    const uint NIF_TIP            = 0x04;
    const uint NOTIFYICON_VERSION_4 = 4;
    const uint WM_TRAYICON        = 0x0401;
    const uint WM_RBUTTONUP       = 0x0205;
    const uint MF_STRING          = 0x00;
    const uint MF_SEPARATOR       = 0x800;
    const uint TPM_LEFTALIGN      = 0x0000;
    const uint TPM_BOTTOMALIGN    = 0x0020;
    const uint TPM_RETURNCMD      = 0x0100;
    const uint IMAGE_ICON         = 1;
    const uint LR_LOADFROMFILE    = 0x10;
    const uint LR_DEFAULTSIZE     = 0x40;
    const nuint SUBCLASS_ID       = 2;
    const nuint ID_AUTOSTART      = 1;
    const nuint ID_EXIT           = 2;

    readonly IntPtr      _hwnd;
    readonly IntPtr      _hIcon;
    readonly SUBCLASSPROC _proc;
    readonly Func<bool>  _getAutostart;
    readonly Action      _toggleAutostart;
    readonly Action      _exit;

    public TrayIconService(IntPtr hwnd, string iconPath,
        Func<bool> getAutostart, Action toggleAutostart, Action exit)
    {
        _hwnd            = hwnd;
        _getAutostart    = getAutostart;
        _toggleAutostart = toggleAutostart;
        _exit            = exit;

        _hIcon = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);

        var nid = MakeNid();
        nid.uFlags          = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        nid.uCallbackMessage = WM_TRAYICON;
        nid.hIcon           = _hIcon;
        nid.szTip           = "Media Buttons";
        Shell_NotifyIcon(NIM_ADD, ref nid);

        nid.uVersion = NOTIFYICON_VERSION_4;
        Shell_NotifyIcon(NIM_SETVERSION, ref nid);

        _proc = SubclassProc;
        SetWindowSubclass(hwnd, _proc, SUBCLASS_ID, 0);
    }

    public void Dispose()
    {
        RemoveWindowSubclass(_hwnd, _proc, SUBCLASS_ID);
        var nid = MakeNid();
        Shell_NotifyIcon(NIM_DELETE, ref nid);
        if (_hIcon != IntPtr.Zero) DestroyIcon(_hIcon);
    }

    NOTIFYICONDATA MakeNid() => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd   = _hwnd,
        uID    = 1,
    };

    nint SubclassProc(IntPtr hWnd, uint uMsg, nuint wParam, nint lParam, nuint uIdSubclass, nuint dwRefData)
    {
        if (uMsg == WM_TRAYICON && (lParam & 0xFFFF) == WM_RBUTTONUP)
            ShowMenu();
        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    void ShowMenu()
    {
        GetCursorPos(out var pt);
        SetForegroundWindow(_hwnd);
        var menu = CreatePopupMenu();
        AppendMenu(menu, MF_STRING, ID_AUTOSTART,
            _getAutostart() ? "Автозапуск: ✓ выключить" : "Автозапуск: включить");
        AppendMenu(menu, MF_SEPARATOR, 0, null);
        AppendMenu(menu, MF_STRING, ID_EXIT, "Выход");
        var cmd = (nuint)TrackPopupMenu(menu,
            TPM_LEFTALIGN | TPM_BOTTOMALIGN | TPM_RETURNCMD, pt.X, pt.Y, 0, _hwnd, IntPtr.Zero);
        DestroyMenu(menu);
        if (cmd == ID_AUTOSTART) _toggleAutostart();
        else if (cmd == ID_EXIT) _exit();
    }
}
