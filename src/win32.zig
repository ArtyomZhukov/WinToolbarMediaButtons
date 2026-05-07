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

// user32 + shell32 — loaded dynamically by initWin32()
pub var setDpiAwareness : *const fn (HANDLE)                                                                                    callconv(.winapi) BOOL    = undefined;
pub var getMsg          : *const fn (*MSG, HWND, UINT, UINT)                                                                    callconv(.winapi) BOOL    = undefined;
pub var translate       : *const fn (*const MSG)                                                                                callconv(.winapi) BOOL    = undefined;
pub var dispatch        : *const fn (*const MSG)                                                                                callconv(.winapi) LRESULT = undefined;
pub var postQuit        : *const fn (INT)                                                                                       callconv(.winapi) void    = undefined;
pub var defWndProc      : *const fn (HWND, UINT, WPARAM, LPARAM)                                                               callconv(.winapi) LRESULT = undefined;
pub var registerClass   : *const fn (*const WNDCLASSEXW)                                                                       callconv(.winapi) ATOM    = undefined;
pub var createWindow    : *const fn (DWORD, [*:0]const WCHAR, ?[*:0]const WCHAR, DWORD, INT, INT, INT, INT, HWND, HMENU, HINSTANCE, ?*anyopaque) callconv(.winapi) HWND = undefined;
pub var findWindow      : *const fn ([*:0]const WCHAR, ?[*:0]const WCHAR)                                                     callconv(.winapi) HWND    = undefined;
pub var setWindowPos    : *const fn (HWND, HWND, INT, INT, INT, INT, UINT)                                                    callconv(.winapi) BOOL    = undefined;
pub var getWindowRect   : *const fn (HWND, *RECT)                                                                             callconv(.winapi) BOOL    = undefined;
pub var trackMouse      : *const fn (*TRACKMOUSEEVENT)                                                                        callconv(.winapi) BOOL    = undefined;
pub var getCursorPos    : *const fn (*POINT)                                                                                  callconv(.winapi) BOOL    = undefined;
pub var screenToClient  : *const fn (HWND, *POINT)                                                                           callconv(.winapi) BOOL    = undefined;
pub var loadCursor      : *const fn (HINSTANCE, usize)                                                                       callconv(.winapi) HCURSOR = undefined;
pub var getWindowLong   : *const fn (HWND, INT)                                                                              callconv(.winapi) LONG    = undefined;
pub var setWindowLong   : *const fn (HWND, INT, LONG)                                                                        callconv(.winapi) LONG    = undefined;
pub var setParent       : *const fn (HWND, HWND)                                                                             callconv(.winapi) HWND    = undefined;
pub var getClientRect   : *const fn (HWND, *RECT)                                                                            callconv(.winapi) BOOL    = undefined;
pub var getDpiForWindow : *const fn (HWND)                                                                                   callconv(.winapi) UINT    = undefined;
pub var keybdEvent      : *const fn (BYTE, BYTE, DWORD, usize)                                                              callconv(.winapi) void    = undefined;
pub var setCapture      : *const fn (HWND)                                                                                   callconv(.winapi) HWND    = undefined;
pub var releaseCapture  : *const fn ()                                                                                       callconv(.winapi) BOOL    = undefined;
pub var setTimer        : *const fn (HWND, usize, UINT, ?*anyopaque)                                                        callconv(.winapi) usize   = undefined;
pub var CreatePopupMenu          : *const fn ()                                                                             callconv(.winapi) HMENU   = undefined;
pub var AppendMenuA              : *const fn (HMENU, UINT, usize, ?[*:0]const u8)                                          callconv(.winapi) BOOL    = undefined;
pub var TrackPopupMenu           : *const fn (HMENU, UINT, INT, INT, INT, HWND, ?*anyopaque)                               callconv(.winapi) BOOL    = undefined;
pub var DestroyMenu              : *const fn (HMENU)                                                                       callconv(.winapi) BOOL    = undefined;
pub var SetForegroundWindow      : *const fn (HWND)                                                                        callconv(.winapi) BOOL    = undefined;

pub fn initWin32() void {
    const huser32 = LoadLibraryA("user32.dll") orelse return;
    setDpiAwareness  = @ptrCast(GetProcAddress(huser32, "SetProcessDpiAwarenessContext").?);
    getMsg           = @ptrCast(GetProcAddress(huser32, "GetMessageW").?);
    translate        = @ptrCast(GetProcAddress(huser32, "TranslateMessage").?);
    dispatch         = @ptrCast(GetProcAddress(huser32, "DispatchMessageW").?);
    postQuit         = @ptrCast(GetProcAddress(huser32, "PostQuitMessage").?);
    defWndProc       = @ptrCast(GetProcAddress(huser32, "DefWindowProcW").?);
    registerClass    = @ptrCast(GetProcAddress(huser32, "RegisterClassExW").?);
    createWindow     = @ptrCast(GetProcAddress(huser32, "CreateWindowExW").?);
    findWindow       = @ptrCast(GetProcAddress(huser32, "FindWindowW").?);
    setWindowPos     = @ptrCast(GetProcAddress(huser32, "SetWindowPos").?);
    getWindowRect    = @ptrCast(GetProcAddress(huser32, "GetWindowRect").?);
    trackMouse       = @ptrCast(GetProcAddress(huser32, "TrackMouseEvent").?);
    getCursorPos     = @ptrCast(GetProcAddress(huser32, "GetCursorPos").?);
    screenToClient   = @ptrCast(GetProcAddress(huser32, "ScreenToClient").?);
    loadCursor       = @ptrCast(GetProcAddress(huser32, "LoadCursorW").?);
    getWindowLong    = @ptrCast(GetProcAddress(huser32, "GetWindowLongW").?);
    setWindowLong    = @ptrCast(GetProcAddress(huser32, "SetWindowLongW").?);
    setParent        = @ptrCast(GetProcAddress(huser32, "SetParent").?);
    getClientRect    = @ptrCast(GetProcAddress(huser32, "GetClientRect").?);
    getDpiForWindow  = @ptrCast(GetProcAddress(huser32, "GetDpiForWindow").?);
    keybdEvent       = @ptrCast(GetProcAddress(huser32, "keybd_event").?);
    setCapture       = @ptrCast(GetProcAddress(huser32, "SetCapture").?);
    releaseCapture   = @ptrCast(GetProcAddress(huser32, "ReleaseCapture").?);
    setTimer         = @ptrCast(GetProcAddress(huser32, "SetTimer").?);
    CreatePopupMenu         = @ptrCast(GetProcAddress(huser32, "CreatePopupMenu").?);
    AppendMenuA             = @ptrCast(GetProcAddress(huser32, "AppendMenuA").?);
    TrackPopupMenu          = @ptrCast(GetProcAddress(huser32, "TrackPopupMenu").?);
    DestroyMenu             = @ptrCast(GetProcAddress(huser32, "DestroyMenu").?);
    SetForegroundWindow     = @ptrCast(GetProcAddress(huser32, "SetForegroundWindow").?);
}


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
