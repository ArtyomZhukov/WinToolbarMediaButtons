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
    if (ico_bytes.len < 6) return null;
    const count: u16 = @as(u16, ico_bytes[4]) | (@as(u16, ico_bytes[5]) << 8);
    if (count == 0) return null;

    // Выбираем лучшее изображение: предпочитаем 32x32, иначе берём наибольшее
    var best_offset: u32 = 0;
    var best_size:   u32 = 0;
    var best_score:  i32 = -1;

    var i: u16 = 0;
    while (i < count) : (i += 1) {
        const base = 6 + @as(usize, i) * 16;
        if (base + 16 > ico_bytes.len) break;
        const width        = ico_bytes[base + 0];  // 0 = 256px
        const bytes_in_res: u32 = @as(u32, ico_bytes[base+8])  | (@as(u32, ico_bytes[base+9])  << 8) |
                                  (@as(u32, ico_bytes[base+10]) << 16) | (@as(u32, ico_bytes[base+11]) << 24);
        const img_offset: u32   = @as(u32, ico_bytes[base+12]) | (@as(u32, ico_bytes[base+13]) << 8) |
                                  (@as(u32, ico_bytes[base+14]) << 16) | (@as(u32, ico_bytes[base+15]) << 24);
        const score: i32   = if (width == 32) 1000 else @intCast(width);
        if (score > best_score) {
            best_score  = score;
            best_offset = img_offset;
            best_size   = bytes_in_res;
        }
    }

    if (best_size == 0 or best_offset + best_size > ico_bytes.len) return null;

    return w.CreateIconFromResourceEx(
        @ptrCast(ico_bytes[best_offset..].ptr),
        best_size,
        1,           // fIcon = TRUE
        0x00030000,  // dwVersion — Windows 3.x DIB format
        0, 0,        // desired size: 0 = default
        0,           // LR_DEFAULTCOLOR
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

    // Tip: "Media Buttons"
    const tip = w.L("Media Buttons");
    @memcpy(g_nid.szTip[0..tip.len], tip);

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

    const autostart_text = if (isAutostartEnabled())
        w.L("Автозапуск: выключить")
    else
        w.L("Автозапуск: включить");

    _ = w.AppendMenuW(menu, MF_STRING, ID_AUTOSTART, autostart_text);
    _ = w.AppendMenuW(menu, MF_SEPARATOR, 0, null);
    _ = w.AppendMenuW(menu, MF_STRING, ID_EXIT, w.L("Выход"));

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
