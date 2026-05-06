// Win32 types and extern declarations, shared across modules.

pub const HWND      = ?*opaque {};
pub const HINSTANCE = ?*opaque {};
pub const HMENU     = ?*opaque {};
pub const HICON     = ?*opaque {};
pub const HCURSOR   = ?*opaque {};
pub const HBRUSH    = ?*opaque {};
pub const HANDLE    = ?*opaque {};
pub const HMODULE   = ?*opaque {};

pub const BOOL    = i32;
pub const UINT    = u32;
pub const DWORD   = u32;
pub const INT     = i32;
pub const LONG    = i32;
pub const ULONG   = u32;
pub const WORD    = u16;
pub const BYTE    = u8;
pub const WPARAM  = usize;
pub const LPARAM  = isize;
pub const LRESULT = isize;
pub const ATOM    = u16;
pub const WCHAR   = u16;

pub const TRUE  : BOOL = 1;
pub const FALSE : BOOL = 0;

pub const WM_DESTROY     = 0x0002;
pub const WM_TIMER       = 0x0113;
pub const WM_NCHITTEST   = 0x0084;
pub const WM_MOUSEMOVE   = 0x0200;
pub const WM_LBUTTONDOWN = 0x0201;
pub const WM_LBUTTONUP   = 0x0202;
pub const WM_RBUTTONDOWN = 0x0204;
pub const WM_MOUSELEAVE  = 0x02A3;
pub const WM_QUIT        = 0x0012;
pub const WM_APP         = 0x8000;
pub const WM_TRAY        = WM_APP + 1;

pub const WS_CHILD        : DWORD = 0x40000000;
pub const WS_POPUP        : DWORD = 0x80000000;
pub const WS_VISIBLE      : DWORD = 0x10000000;
pub const WS_CLIPCHILDREN : DWORD = 0x02000000;
pub const WS_CLIPSIBLINGS : DWORD = 0x04000000;

pub const WS_EX_TOOLWINDOW : DWORD = 0x00000080;
pub const WS_EX_NOACTIVATE : DWORD = 0x08000000;

pub const HTTRANSPARENT : LRESULT = -1;
pub const HTCLIENT      : LRESULT = 1;

pub const CS_HREDRAW : UINT = 0x0002;
pub const CS_VREDRAW : UINT = 0x0001;

pub const CW_USEDEFAULT : INT = @bitCast(@as(u32, 0x80000000));

pub const POINT = extern struct { x: LONG, y: LONG };
pub const RECT  = extern struct { left: LONG, top: LONG, right: LONG, bottom: LONG };
pub const SIZE  = extern struct { cx: LONG, cy: LONG };

pub const MSG = extern struct {
    hwnd:     HWND,
    message:  UINT,
    wParam:   WPARAM,
    lParam:   LPARAM,
    time:     DWORD,
    pt:       POINT,
    lPrivate: DWORD,
};

pub const WNDCLASSEXW = extern struct {
    cbSize:        UINT,
    style:         UINT,
    lpfnWndProc:   *const fn (HWND, UINT, WPARAM, LPARAM) callconv(.winapi) LRESULT,
    cbClsExtra:    INT,
    cbWndExtra:    INT,
    hInstance:     HINSTANCE,
    hIcon:         HICON,
    hCursor:       HCURSOR,
    hbrBackground: HBRUSH,
    lpszMenuName:  ?[*:0]const WCHAR,
    lpszClassName: [*:0]const WCHAR,
    hIconSm:       HICON,
};

// ── kernel32 ──────────────────────────────────────────────────────────────────
extern "kernel32" fn ExitProcess(UINT) callconv(.winapi) noreturn;
extern "kernel32" fn GetModuleHandleW(?[*:0]const WCHAR) callconv(.winapi) HMODULE;
extern "kernel32" fn LoadLibraryW([*:0]const WCHAR) callconv(.winapi) HMODULE;
extern "kernel32" fn GetProcAddress(HMODULE, [*:0]const u8) callconv(.winapi) ?*anyopaque;
extern "user32"   fn SetProcessDpiAwarenessContext(HANDLE) callconv(.winapi) BOOL;

// ── user32 ────────────────────────────────────────────────────────────────────
extern "user32" fn GetMessageW(*MSG, HWND, UINT, UINT) callconv(.winapi) BOOL;
extern "user32" fn TranslateMessage(*const MSG) callconv(.winapi) BOOL;
extern "user32" fn DispatchMessageW(*const MSG) callconv(.winapi) LRESULT;
extern "user32" fn PostQuitMessage(INT) callconv(.winapi) void;
extern "user32" fn DefWindowProcW(HWND, UINT, WPARAM, LPARAM) callconv(.winapi) LRESULT;
extern "user32" fn RegisterClassExW(*const WNDCLASSEXW) callconv(.winapi) ATOM;
extern "user32" fn CreateWindowExW(DWORD, [*:0]const WCHAR, [*:0]const WCHAR, DWORD, INT, INT, INT, INT, HWND, HMENU, HINSTANCE, ?*anyopaque) callconv(.winapi) HWND;
extern "user32" fn FindWindowW([*:0]const WCHAR, ?[*:0]const WCHAR) callconv(.winapi) HWND;
extern "user32" fn SetWindowPos(HWND, HWND, INT, INT, INT, INT, UINT) callconv(.winapi) BOOL;
extern "user32" fn GetWindowRect(HWND, *RECT) callconv(.winapi) BOOL;
extern "user32" fn TrackMouseEvent(*TRACKMOUSEEVENT) callconv(.winapi) BOOL;
extern "user32" fn GetCursorPos(*POINT) callconv(.winapi) BOOL;
extern "user32" fn ScreenToClient(HWND, *POINT) callconv(.winapi) BOOL;
extern "user32" fn LoadCursorW(HINSTANCE, usize) callconv(.winapi) HCURSOR;
extern "user32" fn SetCursor(HCURSOR) callconv(.winapi) HCURSOR;
extern "user32" fn GetWindowLongW(HWND, INT) callconv(.winapi) LONG;
extern "user32" fn SetWindowLongW(HWND, INT, LONG) callconv(.winapi) LONG;
extern "user32" fn SetParent(HWND, HWND) callconv(.winapi) HWND;
extern "user32" fn GetClientRect(HWND, *RECT) callconv(.winapi) BOOL;
extern "user32" fn GetDpiForWindow(HWND) callconv(.winapi) UINT;
extern "user32" fn keybd_event(BYTE, BYTE, DWORD, usize) callconv(.winapi) void;
extern "user32" fn SetCapture(HWND) callconv(.winapi) HWND;
extern "user32" fn ReleaseCapture() callconv(.winapi) BOOL;
extern "user32" fn SetTimer(HWND, usize, UINT, ?*anyopaque) callconv(.winapi) usize;

pub const TME_LEAVE : DWORD = 0x00000002;
pub const TRACKMOUSEEVENT = extern struct {
    cbSize:      DWORD,
    dwFlags:     DWORD,
    hwndTrack:   HWND,
    dwHoverTime: DWORD,
};

// SWP flags
pub const SWP_NOACTIVATE   : UINT = 0x0010;
pub const SWP_NOZORDER     : UINT = 0x0004;
pub const SWP_SHOWWINDOW   : UINT = 0x0040;
pub const SWP_FRAMECHANGED : UINT = 0x0020;

pub const GWL_STYLE : INT = -16;

// Keyboard
pub const KEYEVENTF_KEYUP     : DWORD = 0x0002;
pub const VK_VOLUME_MUTE      : UINT  = 0xAD;
pub const VK_MEDIA_NEXT_TRACK : UINT  = 0xB0;
pub const VK_MEDIA_PREV_TRACK : UINT  = 0xB1;
pub const VK_MEDIA_PLAY_PAUSE : UINT  = 0xB3;

// Re-export so callers just import win32
pub const exit   = ExitProcess;
pub const getMsg = GetMessageW;
pub const translate = TranslateMessage;
pub const dispatch  = DispatchMessageW;
pub const postQuit   = PostQuitMessage;
pub const quit       = postQuit;
pub const defWndProc = DefWindowProcW;
pub const getModuleHandle = GetModuleHandleW;
pub const registerClass = RegisterClassExW;
pub const createWindow  = CreateWindowExW;
pub const findWindow    = FindWindowW;
pub const setWindowPos  = SetWindowPos;
pub const getWindowRect = GetWindowRect;
pub const trackMouse    = TrackMouseEvent;
pub const getCursorPos  = GetCursorPos;
pub const screenToClient = ScreenToClient;
pub const loadCursor     = LoadCursorW;
pub const loadLibrary    = LoadLibraryW;
pub const getProcAddress = GetProcAddress;
pub const setDpiAwareness = SetProcessDpiAwarenessContext;

// DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = (HANDLE)(LONG_PTR)-4
pub const DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2: HANDLE =
    @ptrFromInt(@as(usize, @bitCast(@as(isize, -4))));
pub const getWindowLong  = GetWindowLongW;
pub const setWindowLong  = SetWindowLongW;
pub const setParent      = SetParent;
pub const getClientRect    = GetClientRect;
pub const getDpiForWindow  = GetDpiForWindow;
pub const keybdEvent       = keybd_event;
pub const setCapture       = SetCapture;
pub const releaseCapture   = ReleaseCapture;
pub const setTimer         = SetTimer;

// Special Z-order values for SetWindowPos
pub const HWND_BOTTOM: HWND = @ptrFromInt(1);
pub const HWND_TOP:    HWND = @ptrFromInt(0);
