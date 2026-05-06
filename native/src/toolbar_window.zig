const w     = @import("win32.zig");
const tray  = @import("tray.zig");
const comp  = @import("composition.zig");
const rend  = @import("renderer.zig");
const audio = @import("audio.zig");
const smtc  = @import("smtc.zig");
const std   = @import("std");

// Toolbar dimensions (logical pixels, same as C# version)
pub const TOOLBAR_H : w.INT = 40;
pub const BTN_W     : w.INT = 40;
pub const BTN_COUNT : w.INT = 3;   // prev / play / next
pub const SEP_W     : w.INT = 1;
pub const SLIDER_W  : w.INT = 120;
pub const VOL_BTN_W : w.INT = 32;
pub const TOOLBAR_W : w.INT = BTN_W * BTN_COUNT + SEP_W + SLIDER_W + VOL_BTN_W;

const CLASS_NAME = std.unicode.utf8ToUtf16LeStringLiteral("WinToolbarMediaButtons");

var g_hwnd      : w.HWND     = null;
var g_compositor: ?*anyopaque = null;
var g_target    : ?*anyopaque = null;
var g_root_vis  : ?*anyopaque = null;
var g_dpi       : w.INT = 96;
var g_btn_size  : w.INT = 32;  // physical button side (square)
var g_gap       : w.INT = 2;   // physical gap between button slots
var g_margin_l  : w.INT = 0;   // physical left margin before first button
var g_dragging      : bool  = false;
var g_silence_ticks : u32   = 999;  // ticks since last non-zero peak; 999=unknown/paused

fn hitTest(x: w.INT) rend.HitZone {
    const ml  = g_margin_l;
    if (x < ml) return .none;
    const rx  = x - ml;
    const b   = g_btn_size;
    const sep = @max(1, @divTrunc(g_dpi, 96));
    if (rx < b          ) return .prev;
    if (rx < b * 2      ) return .play;
    if (rx < b * 3      ) return .next;
    if (rx < b * 3 + sep) return .none;
    if (rx < b * 7 + sep) return .slider;
    if (rx < b * 8 + sep) return .vol;
    return .none;  // right margin
}

fn sliderVolume(x: w.INT) f32 {
    const sep     = @max(1, @divTrunc(g_dpi, 96));
    const sld_x0  = g_margin_l + 3 * g_btn_size + sep;
    const sld_w   = 4 * g_btn_size;
    const rel_x   = x - sld_x0;
    if (rel_x <= 0)     return 0;
    if (rel_x >= sld_w) return 1;
    return @as(f32, @floatFromInt(rel_x)) / @as(f32, @floatFromInt(sld_w));
}

fn sendMediaKey(vk: w.UINT) void {
    w.keybdEvent(@truncate(vk), 0, 0, 0);
    w.keybdEvent(@truncate(vk), 0, w.KEYEVENTF_KEYUP, 0);
}

// ── DispatcherQueue ───────────────────────────────────────────────────────────

const DispatcherQueueOptions = extern struct {
    dwSize:        w.DWORD,
    threadType:    w.INT,
    apartmentType: w.INT,
};

// ── Window creation ───────────────────────────────────────────────────────────

fn tryCreateDispatcherQueue() void {
    const Fn = *const fn (DispatcherQueueOptions, *?*anyopaque) callconv(.winapi) w.LONG;
    const lib = w.loadLibrary(std.unicode.utf8ToUtf16LeStringLiteral("CoreMessaging.dll")) orelse return;
    const f = w.getProcAddress(lib, "CreateDispatcherQueueController") orelse return;
    var dqc: ?*anyopaque = null;
    _ = @as(Fn, @ptrCast(f))(.{
        .dwSize        = @sizeOf(DispatcherQueueOptions),
        .threadType    = 2, // DQTYPE_THREAD_CURRENT
        .apartmentType = 1, // DQTAT_COM_STA
    }, &dqc);
}

pub fn create(hinstance: w.HINSTANCE) !w.HWND {
    // Required for Windows.UI.Composition on this thread
    tryCreateDispatcherQueue();
    smtc.init();

    // Register window class
    const wc = w.WNDCLASSEXW{
        .cbSize        = @sizeOf(w.WNDCLASSEXW),
        .style         = 0,
        .lpfnWndProc   = wndProc,
        .cbClsExtra    = 0,
        .cbWndExtra    = 0,
        .hInstance     = hinstance,
        .hIcon         = null,
        .hCursor       = w.loadCursor(null, 32512), // IDC_ARROW
        .hbrBackground = null,
        .lpszMenuName  = null,
        .lpszClassName = CLASS_NAME,
        .hIconSm       = null,
    };
    _ = w.registerClass(&wc);

    // Find taskbar
    const taskbar = w.findWindow(std.unicode.utf8ToUtf16LeStringLiteral("Shell_TrayWnd"), null);
    if (taskbar == null) return error.NoTaskbar;

    // Compute DPI-aware layout before creating the window
    const dpi: w.INT = @intCast(w.getDpiForWindow(taskbar));
    g_dpi = dpi;

    var client: w.RECT = undefined;
    _ = w.getClientRect(taskbar, &client);
    const actual_h = client.bottom;

    // btn_size = taskbar height − 2×4dp (square buttons)
    const pad_phys  = @max(1, @divTrunc(4 * dpi, 96));
    const btn_size  = actual_h - 2 * pad_phys;
    g_btn_size      = btn_size;
    const sep_phys: w.INT  = @max(1, @divTrunc(dpi, 96));
    const gap_phys: w.INT  = @max(1, @divTrunc(4 * dpi, 96));  // 4dp between slots
    const margin_r: w.INT  = @max(2, @divTrunc(8 * dpi, 96));  // 8dp right margin
    const margin_l: w.INT  = margin_r;                          // symmetric left margin
    g_gap      = gap_phys;
    g_margin_l = margin_l;
    // Layout: [margin_l][prev][play][next][sep][slider×4][vol][margin_r]
    const toolbar_w_phys = 8 * btn_size + sep_phys + margin_l + margin_r;

    var taskbar_rect: w.RECT = undefined;
    _ = w.getWindowRect(taskbar, &taskbar_rect);

    // Create as WS_POPUP — CreateDesktopWindowTarget requires top-level
    const hwnd = w.createWindow(
        w.WS_EX_TOOLWINDOW | w.WS_EX_NOACTIVATE,
        CLASS_NAME,
        std.unicode.utf8ToUtf16LeStringLiteral(""),
        w.WS_POPUP | w.WS_VISIBLE | w.WS_CLIPCHILDREN | w.WS_CLIPSIBLINGS,
        taskbar_rect.left,
        taskbar_rect.top,
        toolbar_w_phys,
        actual_h,
        null,
        null,
        hinstance,
        null,
    );
    if (hwnd == null) return error.CreateWindowFailed;

    // Init Windows.UI.Composition
    const compositor = comp.activateCompositor() orelse return error.CompositorFailed;
    g_compositor = compositor;

    const target = comp.createDesktopWindowTarget(compositor, hwnd, 1) orelse return error.TargetFailed;
    g_target = target;

    // Single SpriteVisual filling the whole window (brush set in Phase 5)
    const spr = comp.createSpriteVisual(compositor) orelse return error.SprVisualFailed;
    comp.vis2SetRelativeSize(spr);
    comp.targetSetRoot(target, spr);
    g_root_vis = spr;

    // Convert WS_POPUP → WS_CHILD and reparent to taskbar
    _ = w.setParent(hwnd, taskbar);
    const style_u: w.DWORD = @bitCast(w.getWindowLong(hwnd, w.GWL_STYLE));
    const new_style: w.LONG = @bitCast(
        (style_u & ~w.WS_POPUP) | w.WS_CHILD | w.WS_VISIBLE | w.WS_CLIPCHILDREN | w.WS_CLIPSIBLINGS
    );
    _ = w.setWindowLong(hwnd, w.GWL_STYLE, new_style);

    _ = w.setWindowPos(hwnd, null, 0, 0, toolbar_w_phys, actual_h,
        w.SWP_NOACTIVATE | w.SWP_FRAMECHANGED);

    _ = rend.init(compositor, spr, @intCast(toolbar_w_phys), @intCast(actual_h), @intCast(btn_size), @intCast(gap_phys), @intCast(margin_l));
    rend.render();

    _ = w.setTimer(hwnd, 1, 500, null);

    g_hwnd = hwnd;
    return hwnd;
}

// ── WndProc ───────────────────────────────────────────────────────────────────

fn wndProc(hwnd: w.HWND, msg: w.UINT, wp: w.WPARAM, lp: w.LPARAM) callconv(.winapi) w.LRESULT {
    switch (msg) {
        w.WM_NCHITTEST => return w.HTCLIENT,

        w.WM_MOUSEMOVE => {
            var tme = w.TRACKMOUSEEVENT{
                .cbSize      = @sizeOf(w.TRACKMOUSEEVENT),
                .dwFlags     = w.TME_LEAVE,
                .hwndTrack   = hwnd,
                .dwHoverTime = 0,
            };
            _ = w.trackMouse(&tme);
            const x: w.INT = @as(i16, @truncate(lp));
            if (g_dragging) {
                audio.setVolume(sliderVolume(x));
                rend.render();
            } else {
                if (rend.setHover(hitTest(x))) rend.render();
            }
        },

        w.WM_TRAY  => tray.handleTrayMessage(lp),

        w.WM_TIMER => {
            smtc.poll();
            if (smtc.isAvailable()) {
                _ = rend.setPlaying(smtc.isPlaying());
            } else {
                const peak = audio.getPeak();
                if (peak > 0.001) {
                    g_silence_ticks = 0;
                    _ = rend.setPlaying(true);
                } else {
                    if (g_silence_ticks < 6) g_silence_ticks += 1;
                    if (g_silence_ticks >= 6) _ = rend.setPlaying(false);
                }
            }
            rend.render();
        },

        w.WM_MOUSELEAVE => {
            if (!g_dragging) {
                if (rend.setHover(.none)) rend.render();
            }
        },

        w.WM_LBUTTONDOWN => {
            const x: w.INT = @as(i16, @truncate(lp));
            switch (hitTest(x)) {
                .prev   => sendMediaKey(w.VK_MEDIA_PREV_TRACK),
                .play   => {
                    sendMediaKey(w.VK_MEDIA_PLAY_PAUSE);
                    rend.togglePlaying();
                    rend.render();
                },
                .next   => sendMediaKey(w.VK_MEDIA_NEXT_TRACK),
                .slider => {
                    g_dragging = true;
                    _ = w.setCapture(hwnd);
                    audio.setVolume(sliderVolume(x));
                    rend.render();
                },
                .vol    => {
                    audio.toggleMute();
                    rend.render();
                },
                else    => {},
            }
        },

        w.WM_LBUTTONUP => {
            if (g_dragging) {
                g_dragging = false;
                _ = w.releaseCapture();
                rend.render();
            }
        },

        // WM_DESTROY не постим WM_QUIT — тулбар не должен выходить
        // если taskbar перезапускается. Выход только через tray меню.

        else => {},
    }
    return w.defWndProc(hwnd, msg, wp, lp);
}
