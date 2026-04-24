using System.Runtime.InteropServices;

namespace WinToolbarMediaButtons.Services;

sealed class AppBarService : IDisposable
{
    [DllImport("comctl32.dll")]
    static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, nuint uIdSubclass, nuint dwRefData);

    [DllImport("comctl32.dll")]
    static extern bool RemoveWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, nuint uIdSubclass);

    [DllImport("comctl32.dll")]
    static extern nint DefSubclassProc(IntPtr hWnd, uint uMsg, nuint wParam, nint lParam);

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool SetProp(IntPtr hWnd, string lpString, IntPtr hData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr RemoveProp(IntPtr hWnd, string lpString);

    [DllImport("shell32.dll")]
    static extern IntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    [DllImport("user32.dll")]
    static extern bool RegisterShellHookWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern bool DeregisterShellHookWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern uint RegisterWindowMessage(string lpString);

    delegate nint SUBCLASSPROC(IntPtr hWnd, uint uMsg, nuint wParam, nint lParam, nuint uIdSubclass, nuint dwRefData);
    delegate void WinEventDelegate(IntPtr hWinEventHook, uint @event, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [StructLayout(LayoutKind.Sequential)]
    struct APPBARDATA
    {
        public uint   cbSize;
        public IntPtr hWnd;
        public uint   uCallbackMessage;
        public uint   uEdge;
        public int    RcLeft, RcTop, RcRight, RcBottom; // RECT
        public IntPtr lParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WINDOWPOS
    {
        public IntPtr hwnd, hwndInsertAfter;
        public int x, y, cx, cy;
        public uint flags;
    }

    const uint ABM_NEW              = 0x00000000;
    const uint ABM_REMOVE           = 0x00000001;
    const uint ABN_FULLSCREENAPP    = 0x00000002;
    const uint WM_WINDOWPOSCHANGING    = 0x0046;
    const uint WM_WINDOWPOSCHANGED     = 0x0047;
    const uint SWP_NOMOVE              = 0x0002;
    const uint SWP_NOSIZE              = 0x0001;
    const uint SWP_NOACTIVATE          = 0x0010;
    const uint SWP_NOZORDER            = 0x0004;
    const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    const uint WINEVENT_OUTOFCONTEXT   = 0x0000;
    const int  HSHELL_WINDOWACTIVATED  = 4;
    static readonly IntPtr HWND_TOPMOST = new(-1);

    readonly IntPtr           _hwnd;
    readonly SUBCLASSPROC     _subclassProc;
    readonly WinEventDelegate _winEventProc;
    readonly IntPtr           _winEventHook;
    readonly uint             _shellHookMsg;
    readonly uint             _appBarMsg;
    bool _reordering;

    public AppBarService(IntPtr hwnd)
    {
        _hwnd = hwnd;

        _subclassProc = SubclassProc;
        SetWindowSubclass(hwnd, _subclassProc, 1, 0);

        // Срабатывает мгновенно при смене foreground-окна (Пуск, Quick Settings и т.д.)
        // WM_WINDOWPOSCHANGING не ловит Z-band реструктуризацию шелла — хук нужен как дополнение
        _winEventProc = OnForegroundChanged;
        _winEventHook = SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _winEventProc, 0, 0, WINEVENT_OUTOFCONTEXT);

        // Запрещаем Rude Window Manager опускать наше окно при открытии Пуска/Quick Settings
        SetProp(hwnd, "NonRudeHWND", hwnd);

        // Shell hook: доставляется как обычное window message (синхронно), а не через async WinEventHook
        RegisterShellHookWindow(hwnd);
        _shellHookMsg = RegisterWindowMessage("SHELLHOOK");

        // Регистрируемся как AppBar — переходим в Z-band шелла, вровень с taskbar
        // ABM_SETPOS намеренно не вызываем — work area остаётся неизменной
        _appBarMsg = RegisterWindowMessage("WinToolbarMediaButtons.AppBar");
        var abd = new APPBARDATA
        {
            cbSize           = (uint)Marshal.SizeOf<APPBARDATA>(),
            hWnd             = hwnd,
            uCallbackMessage = _appBarMsg,
        };
        SHAppBarMessage(ABM_NEW, ref abd);
    }

    public void Dispose()
    {
        RemoveWindowSubclass(_hwnd, _subclassProc, 1);
        if (_winEventHook != IntPtr.Zero)
            UnhookWinEvent(_winEventHook);
        RemoveProp(_hwnd, "NonRudeHWND");
        DeregisterShellHookWindow(_hwnd);
        var abd = new APPBARDATA { cbSize = (uint)Marshal.SizeOf<APPBARDATA>(), hWnd = _hwnd };
        SHAppBarMessage(ABM_REMOVE, ref abd);
    }

    void OnForegroundChanged(IntPtr hWinEventHook, uint @event, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        => SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    nint SubclassProc(IntPtr hWnd, uint uMsg, nuint wParam, nint lParam, nuint uIdSubclass, nuint dwRefData)
    {
        if (uMsg == _shellHookMsg && ((int)wParam & 0x7FFF) == HSHELL_WINDOWACTIVATED)
        {
            SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
        else if (uMsg == _appBarMsg)
        {
            // ABN_FULLSCREENAPP (wParam=1 — запустился, 0 — закрылся): при закрытии поднимаемся обратно
            if ((uint)wParam == ABN_FULLSCREENAPP && lParam == 0)
                SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
        else if (uMsg == WM_WINDOWPOSCHANGING)
        {
            var wp = Marshal.PtrToStructure<WINDOWPOS>((IntPtr)lParam);
            if ((wp.flags & SWP_NOZORDER) == 0 && wp.hwndInsertAfter != HWND_TOPMOST)
            {
                wp.flags |= SWP_NOZORDER;
                Marshal.StructureToPtr(wp, (IntPtr)lParam, false);
            }
        }
        else if (uMsg == WM_WINDOWPOSCHANGED && !_reordering)
        {
            _reordering = true;
            SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            _reordering = false;
        }

        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }
}
