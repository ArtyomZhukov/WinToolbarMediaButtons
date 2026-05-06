const w = @import("win32.zig");
const std = @import("std");

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

const MF_STRING  : w.UINT = 0x00000000;
const MF_GRAYED  : w.UINT = 0x00000001;
const MF_SEPARATOR: w.UINT = 0x00000800;
const TPM_RIGHTBUTTON: w.UINT = 0x0002;
const TPM_RETURNCMD:   w.UINT = 0x0100;
const TPM_NONOTIFY:    w.UINT = 0x0080;

const ID_AUTOSTART : w.UINT = 1001;
const ID_EXIT      : w.UINT = 1002;

extern "shell32" fn Shell_NotifyIconW(w.DWORD, *NOTIFYICONDATAW) callconv(.winapi) w.BOOL;
extern "user32"  fn CreatePopupMenu() callconv(.winapi) w.HMENU;
extern "user32"  fn AppendMenuW(w.HMENU, w.UINT, usize, ?[*:0]const w.WCHAR) callconv(.winapi) w.BOOL;
extern "user32"  fn TrackPopupMenu(w.HMENU, w.UINT, w.INT, w.INT, w.INT, w.HWND, ?*anyopaque) callconv(.winapi) w.BOOL;
extern "user32"  fn DestroyMenu(w.HMENU) callconv(.winapi) w.BOOL;
extern "user32"  fn SetForegroundWindow(w.HWND) callconv(.winapi) w.BOOL;
extern "user32"  fn DestroyIcon(w.HICON) callconv(.winapi) w.BOOL;
extern "user32"  fn CreateIconFromResourceEx(?[*]const u8, w.DWORD, w.BOOL, w.DWORD, w.INT, w.INT, w.UINT) callconv(.winapi) w.HICON;

// ICO-файл встроен прямо в бинарь
const ico_bytes = @embedFile("res/app.ico");

fn loadEmbeddedIcon() w.HICON {
    if (ico_bytes.len < 6) return null;
    const count = std.mem.readInt(u16, ico_bytes[4..6], .little);
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
        const bytes_in_res = std.mem.readInt(u32, ico_bytes[base + 8 ..][0..4], .little);
        const img_offset   = std.mem.readInt(u32, ico_bytes[base + 12..][0..4], .little);
        const score: i32   = if (width == 32) 1000 else @intCast(width);
        if (score > best_score) {
            best_score  = score;
            best_offset = img_offset;
            best_size   = bytes_in_res;
        }
    }

    if (best_size == 0 or best_offset + best_size > ico_bytes.len) return null;

    return CreateIconFromResourceEx(
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

var g_nid: NOTIFYICONDATAW = std.mem.zeroes(NOTIFYICONDATAW);
var g_hwnd: w.HWND = null;

pub fn create(hwnd: w.HWND) void {
    g_hwnd = hwnd;
    g_nid = std.mem.zeroes(NOTIFYICONDATAW);
    g_nid.cbSize           = @sizeOf(NOTIFYICONDATAW);
    g_nid.hWnd             = hwnd;
    g_nid.uID              = 1;
    g_nid.uFlags           = NIF_MESSAGE | NIF_ICON | NIF_TIP;
    g_nid.uCallbackMessage = w.WM_TRAY;
    g_nid.hIcon            = loadEmbeddedIcon();

    // Tip: "Media Buttons"
    const tip = std.unicode.utf8ToUtf16LeStringLiteral("Media Buttons");
    @memcpy(g_nid.szTip[0..tip.len], tip);

    _ = Shell_NotifyIconW(NIM_ADD, &g_nid);
}

pub fn destroy() void {
    _ = Shell_NotifyIconW(NIM_DELETE, &g_nid);
}

pub fn handleTrayMessage(lp: w.LPARAM) void {
    const event: w.UINT = @intCast(lp & 0xFFFF);
    if (event == 0x0205) { // WM_RBUTTONUP
        showMenu();
    }
}

fn showMenu() void {
    const menu = CreatePopupMenu() orelse return;
    defer _ = DestroyMenu(menu);

    const autostart_text = if (isAutostartEnabled())
        std.unicode.utf8ToUtf16LeStringLiteral("Автозапуск: выключить")
    else
        std.unicode.utf8ToUtf16LeStringLiteral("Автозапуск: включить");

    _ = AppendMenuW(menu, MF_STRING, ID_AUTOSTART, autostart_text);
    _ = AppendMenuW(menu, MF_SEPARATOR, 0, null);
    _ = AppendMenuW(menu, MF_STRING, ID_EXIT, std.unicode.utf8ToUtf16LeStringLiteral("Выход"));

    var pt: w.POINT = undefined;
    _ = w.getCursorPos(&pt);

    _ = SetForegroundWindow(g_hwnd);
    const cmd = TrackPopupMenu(
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
