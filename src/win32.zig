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
extern "kernel32" fn LoadLibraryA([*:0]const u8) callconv(.winapi) HMODULE;
extern "kernel32" fn GetProcAddress(HMODULE, [*:0]const u8) callconv(.winapi) ?*anyopaque;

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

// kernel32 (static)
pub const exit              = ExitProcess;
pub const getModuleHandle   = GetModuleHandleW;
pub const loadLibrary       = LoadLibraryA;
pub const getProcAddress    = GetProcAddress;

// DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = (HANDLE)(LONG_PTR)-4
pub const DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2: HANDLE =
    @ptrFromInt(@as(usize, @bitCast(@as(isize, -4))));

// user32 — static imports
extern "user32" fn SetProcessDpiAwarenessContext(HANDLE) callconv(.winapi) BOOL;
extern "user32" fn GetMessageW(*MSG, HWND, UINT, UINT) callconv(.winapi) BOOL;
extern "user32" fn DispatchMessageW(*const MSG) callconv(.winapi) LRESULT;
extern "user32" fn PostQuitMessage(INT) callconv(.winapi) void;
extern "user32" fn DefWindowProcW(HWND, UINT, WPARAM, LPARAM) callconv(.winapi) LRESULT;
extern "user32" fn RegisterClassExW(*const WNDCLASSEXW) callconv(.winapi) ATOM;
extern "user32" fn CreateWindowExW(DWORD, [*:0]const WCHAR, ?[*:0]const WCHAR, DWORD, INT, INT, INT, INT, HWND, HMENU, HINSTANCE, ?*anyopaque) callconv(.winapi) HWND;
extern "user32" fn FindWindowW([*:0]const WCHAR, ?[*:0]const WCHAR) callconv(.winapi) HWND;
extern "user32" fn SetWindowPos(HWND, HWND, INT, INT, INT, INT, UINT) callconv(.winapi) BOOL;
extern "user32" fn GetWindowRect(HWND, *RECT) callconv(.winapi) BOOL;
extern "user32" fn TrackMouseEvent(*TRACKMOUSEEVENT) callconv(.winapi) BOOL;
extern "user32" fn GetCursorPos(*POINT) callconv(.winapi) BOOL;
extern "user32" fn LoadCursorW(HINSTANCE, usize) callconv(.winapi) HCURSOR;
extern "user32" fn GetWindowLongW(HWND, INT) callconv(.winapi) LONG;
extern "user32" fn SetWindowLongW(HWND, INT, LONG) callconv(.winapi) LONG;
extern "user32" fn SetParent(HWND, HWND) callconv(.winapi) HWND;
extern "user32" fn GetClientRect(HWND, *RECT) callconv(.winapi) BOOL;
extern "user32" fn GetDpiForWindow(HWND) callconv(.winapi) UINT;
extern "user32" fn keybd_event(BYTE, BYTE, DWORD, usize) callconv(.winapi) void;
extern "user32" fn SetCapture(HWND) callconv(.winapi) HWND;
extern "user32" fn ReleaseCapture() callconv(.winapi) BOOL;
extern "user32" fn SetTimer(HWND, usize, UINT, ?*anyopaque) callconv(.winapi) usize;
pub extern "user32" fn CreatePopupMenu() callconv(.winapi) HMENU;
pub extern "user32" fn AppendMenuA(HMENU, UINT, usize, ?[*:0]const u8) callconv(.winapi) BOOL;
pub extern "user32" fn TrackPopupMenu(HMENU, UINT, INT, INT, INT, HWND, ?*anyopaque) callconv(.winapi) BOOL;
pub extern "user32" fn DestroyMenu(HMENU) callconv(.winapi) BOOL;
pub extern "user32" fn SetForegroundWindow(HWND) callconv(.winapi) BOOL;

pub const setDpiAwareness = SetProcessDpiAwarenessContext;
pub const getMsg          = GetMessageW;
pub const dispatch        = DispatchMessageW;
pub const postQuit        = PostQuitMessage;
pub const defWndProc      = DefWindowProcW;
pub const registerClass   = RegisterClassExW;
pub const createWindow    = CreateWindowExW;
pub const findWindow      = FindWindowW;
pub const setWindowPos    = SetWindowPos;
pub const getWindowRect   = GetWindowRect;
pub const trackMouse      = TrackMouseEvent;
pub const getCursorPos    = GetCursorPos;
pub const loadCursor      = LoadCursorW;
pub const getWindowLong   = GetWindowLongW;
pub const setWindowLong   = SetWindowLongW;
pub const setParent       = SetParent;
pub const getClientRect   = GetClientRect;
pub const getDpiForWindow = GetDpiForWindow;
pub const keybdEvent      = keybd_event;
pub const setCapture      = SetCapture;
pub const releaseCapture  = ReleaseCapture;
pub const setTimer        = SetTimer;


// Comptime UTF-8 → UTF-16LE string literal (replaces std.unicode dependency).
fn countL(comptime s: []const u8) comptime_int {
    var n: comptime_int = 0;
    var i: comptime_int = 0;
    while (i < s.len) {
        const b = s[i];
        if (b & 0x80 == 0)       { n += 1; i += 1; }
        else if (b & 0xE0 == 0xC0) { n += 1; i += 2; }
        else if (b & 0xF0 == 0xE0) { n += 1; i += 3; }
        else                       { n += 2; i += 4; }
    }
    return n;
}
pub fn L(comptime s: []const u8) *const [countL(s):0]u16 {
    return comptime blk: {
        const n = countL(s);
        var buf: [n:0]u16 = undefined;
        var i: usize = 0;
        var j: usize = 0;
        while (i < s.len) {
            const b = s[i];
            if (b & 0x80 == 0) {
                buf[j] = b; i += 1; j += 1;
            } else if (b & 0xE0 == 0xC0) {
                buf[j] = (@as(u16, b & 0x1F) << 6) | (s[i+1] & 0x3F);
                i += 2; j += 1;
            } else if (b & 0xF0 == 0xE0) {
                buf[j] = (@as(u16, b & 0x0F) << 12) | (@as(u16, s[i+1] & 0x3F) << 6) | (s[i+2] & 0x3F);
                i += 3; j += 1;
            } else {
                const cp: u32 = (@as(u32, b & 0x07) << 18) | (@as(u32, s[i+1] & 0x3F) << 12) |
                    (@as(u32, s[i+2] & 0x3F) << 6) | (s[i+3] & 0x3F);
                buf[j]   = @intCast(0xD800 + ((cp - 0x10000) >> 10));
                buf[j+1] = @intCast(0xDC00 + ((cp - 0x10000) & 0x3FF));
                i += 4; j += 2;
            }
        }
        buf[n] = 0;
        const result = buf;
        break :blk &result;
    };
}
