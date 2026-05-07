const w = @import("win32.zig");

// Win32 types for tray/menu
const NOTIFYICONDATAW = extern struct {
    cbSize:           w.DWORD,
    hWnd:             w.HWND,
    uID:              w.UINT,
    uFlags:           w.UINT,
    uCallbackMessage: w.UINT,
    hIcon:            w.HICON,
    szTip:            [128]w.WCHAR,
    dwState:          w.DWORD,
    dwStateMask:      w.DWORD,
    szInfo:           [256]w.WCHAR,
    uVersion:         w.UINT,
    szInfoTitle:      [64]w.WCHAR,
    dwInfoFlags:      w.DWORD,
    guidItem:         [16]u8,
    hBalloonIcon:     w.HICON,
};

const NIM_ADD    : w.DWORD = 0x00000000;
const NIM_DELETE : w.DWORD = 0x00000002;
const NIF_MESSAGE: w.UINT  = 0x00000001;
const NIF_ICON   : w.UINT  = 0x00000002;
const NIF_TIP    : w.UINT  = 0x00000004;

const MF_STRING   : w.UINT = 0x00000000;
const MF_SEPARATOR: w.UINT = 0x00000800;
const TPM_RIGHTBUTTON: w.UINT = 0x0002;
const TPM_RETURNCMD:   w.UINT = 0x0100;
const TPM_NONOTIFY:    w.UINT = 0x0080;

const ID_AUTOSTART : w.UINT = 1001;
const ID_EXIT      : w.UINT = 1002;


// ICO-файл встроен прямо в бинарь
const ico_bytes = @embedFile("res/app.ico");

fn loadEmbeddedIcon() w.HICON {
    // Single image ICO: header(6) + dir_entry(16) + PNG data at offset 22
    return w.CreateIconFromResourceEx(
        @ptrCast(ico_bytes[22..].ptr),
        @intCast(ico_bytes.len - 22),
        1, 0x00030000, 0, 0, 0,
    );
}

// Autostart callbacks (implemented in autostart.zig, wired in main)
pub var isAutostartEnabled: *const fn () bool = undefined;
pub var toggleAutostart:    *const fn () void = undefined;
pub var quitFn:             *const fn () void = undefined;

var g_nid: NOTIFYICONDATAW = undefined;
var g_hwnd: w.HWND = null;

pub fn create(hwnd: w.HWND) void {
    g_hwnd = hwnd;
    @memset(@as(*[@sizeOf(NOTIFYICONDATAW)]u8, @ptrCast(&g_nid)), 0);
    g_nid.cbSize           = @sizeOf(NOTIFYICONDATAW);
    g_nid.hWnd             = hwnd;
    g_nid.uID              = 1;
    g_nid.uFlags           = NIF_MESSAGE | NIF_ICON | NIF_TIP;
    g_nid.uCallbackMessage = w.WM_TRAY;
    g_nid.hIcon            = loadEmbeddedIcon();
    _ = w.Shell_NotifyIconW(NIM_ADD, &g_nid);
}

pub fn destroy() void {
    _ = w.Shell_NotifyIconW(NIM_DELETE, &g_nid);
}

pub fn handleTrayMessage(lp: w.LPARAM) void {
    const event: w.UINT = @intCast(lp & 0xFFFF);
    if (event == 0x0205) { // WM_RBUTTONUP
        showMenu();
    }
}

fn showMenu() void {
    const menu = w.CreatePopupMenu() orelse return;
    defer _ = w.DestroyMenu(menu);

    const autostart_text: [*:0]const u8 = if (isAutostartEnabled())
        "Autostart: off"
    else
        "Autostart: on";

    _ = w.AppendMenuA(menu, MF_STRING, ID_AUTOSTART, autostart_text);
    _ = w.AppendMenuA(menu, MF_SEPARATOR, 0, null);
    _ = w.AppendMenuA(menu, MF_STRING, ID_EXIT, "Quit");

    var pt: w.POINT = undefined;
    _ = w.getCursorPos(&pt);

    _ = w.SetForegroundWindow(g_hwnd);
    const cmd = w.TrackPopupMenu(
        menu,
        TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_NONOTIFY,
        pt.x, pt.y, 0,
        g_hwnd, null,
    );

    if (cmd == @as(w.BOOL, @intCast(ID_AUTOSTART))) {
        toggleAutostart();
    } else if (cmd == @as(w.BOOL, @intCast(ID_EXIT))) {
        quitFn();
    }
}
