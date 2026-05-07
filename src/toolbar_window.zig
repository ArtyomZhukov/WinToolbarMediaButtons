const w    = @import("win32.zig");
const comp = @import("composition.zig");
const rend = @import("renderer.zig");
const audio = @import("audio.zig");

const CLASS_NAME = w.L("WTMB");

var g_btn_size  : w.INT = 32;
var g_gap       : w.INT = 2;
var g_margin_l  : w.INT = 0;
var g_dragging      : bool = false;
var g_silence_ticks : u32  = 999;

fn hitTest(x: w.INT) rend.HitZone {
    const ml = g_margin_l;
    if (x < ml) return .none;
    const rx = x - ml;
    const b  = g_btn_size;
    if (rx < b    ) return .prev;
    if (rx < b * 2) return .play;
    if (rx < b * 3) return .next;
    if (rx < b * 7) return .slider;
    if (rx < b * 8) return .vol;
    return .none;
}

fn sliderVolume(x: w.INT) f32 {
    const sld_x0 = g_margin_l + 3 * g_btn_size;
    const sld_w  = 4 * g_btn_size;
    const rel_x  = x - sld_x0;
    if (rel_x <= 0)     return 0;
    if (rel_x >= sld_w) return 1;
    return @as(f32, @floatFromInt(rel_x)) / @as(f32, @floatFromInt(sld_w));
}

fn sendMediaKey(vk: w.UINT) void {
    w.keybdEvent(@truncate(vk), 0, 0, 0);
    w.keybdEvent(@truncate(vk), 0, w.KEYEVENTF_KEYUP, 0);
}

// ── Window creation ───────────────────────────────────────────────────────────

pub fn create(hinstance: w.HINSTANCE) void {
    // Required for Windows.UI.Composition on this thread
    const lib = w.loadLibrary("CoreMessaging.dll");
    if (lib) |l| {
        const f = w.getProcAddress(l, "CreateDispatcherQueueController");
        if (f) |fn_ptr| {
            const Opts = extern struct { dwSize: w.DWORD, threadType: w.INT, apartmentType: w.INT };
            const Fn = *const fn (Opts, *?*anyopaque) callconv(.winapi) w.LONG;
            var dqc: ?*anyopaque = null;
            _ = @as(Fn, @ptrCast(fn_ptr))(.{ .dwSize = @sizeOf(Opts), .threadType = 2, .apartmentType = 1 }, &dqc);
        }
    }

    const wc = w.WNDCLASSEXW{
        .cbSize        = @sizeOf(w.WNDCLASSEXW),
        .style         = 0,
        .lpfnWndProc   = wndProc,
        .cbClsExtra    = 0,
        .cbWndExtra    = 0,
        .hInstance     = hinstance,
        .hIcon         = null,
        .hCursor       = w.loadCursor(null, 32512),
        .hbrBackground = null,
        .lpszMenuName  = null,
        .lpszClassName = CLASS_NAME,
        .hIconSm       = null,
    };
    _ = w.registerClass(&wc);

    const taskbar = w.findWindow(w.L("Shell_TrayWnd"), null);
    if (taskbar == null) w.exit(1);

    const dpi: w.INT = @intCast(w.getDpiForWindow(taskbar));

    var client: w.RECT = undefined;
    _ = w.getClientRect(taskbar, &client);
    const actual_h = client.bottom;

    const pad_phys  = @max(1, @divTrunc(4 * dpi, 96));
    const btn_size  = actual_h - 2 * pad_phys;
    g_btn_size      = btn_size;
    const gap_phys: w.INT  = @max(1, @divTrunc(4 * dpi, 96));
    const margin_r: w.INT  = @max(2, @divTrunc(8 * dpi, 96));
    const margin_l: w.INT  = margin_r;
    g_gap      = gap_phys;
    g_margin_l = margin_l;
    const toolbar_w_phys = 8 * btn_size + margin_l + margin_r;

    var taskbar_rect: w.RECT = undefined;
    _ = w.getWindowRect(taskbar, &taskbar_rect);

    const hwnd = w.createWindow(
        w.WS_EX_TOOLWINDOW | w.WS_EX_NOACTIVATE,
        CLASS_NAME,
        null,
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
    if (hwnd == null) w.exit(1);

    const compositor = comp.activateCompositor() orelse w.exit(1);
    const target = comp.createDesktopWindowTarget(compositor, hwnd, 1) orelse w.exit(1);
    const spr = comp.createSpriteVisual(compositor) orelse w.exit(1);
    comp.vis2SetRelativeSize(spr);
    comp.targetSetRoot(target, spr);

    _ = w.setParent(hwnd, taskbar);
    const style_u: w.DWORD = @bitCast(w.getWindowLong(hwnd, w.GWL_STYLE));
    const new_style: w.LONG = @bitCast(
        (style_u & ~w.WS_POPUP) | w.WS_CHILD | w.WS_VISIBLE | w.WS_CLIPCHILDREN | w.WS_CLIPSIBLINGS
    );
    _ = w.setWindowLong(hwnd, w.GWL_STYLE, new_style);

    _ = w.setWindowPos(hwnd, null, 0, 0, toolbar_w_phys, actual_h,
        w.SWP_NOACTIVATE | w.SWP_FRAMECHANGED);

    rend.init(compositor, spr, @intCast(toolbar_w_phys), @intCast(actual_h), @intCast(btn_size), @intCast(gap_phys), @intCast(margin_l));
    rend.render();

    _ = w.setTimer(hwnd, 1, 500, null);
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

        w.WM_RBUTTONDOWN => {
            const menu = w.CreatePopupMenu() orelse return w.defWndProc(hwnd, msg, wp, lp);
            defer _ = w.DestroyMenu(menu);
            _ = w.AppendMenuA(menu, 0, 1, "Quit");
            var pt: w.POINT = undefined;
            _ = w.getCursorPos(&pt);
            _ = w.SetForegroundWindow(hwnd);
            if (w.TrackPopupMenu(menu, 0x0002 | 0x0100 | 0x0080, pt.x, pt.y, 0, hwnd, null) == 1) {
                w.postQuit(0);
            }
        },

        w.WM_TIMER => {
            const peak = audio.getPeak();
            if (peak > 0.001) {
                g_silence_ticks = 0;
                _ = rend.setPlaying(true);
            } else {
                if (g_silence_ticks < 6) g_silence_ticks += 1;
                if (g_silence_ticks >= 6) _ = rend.setPlaying(false);
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

        else => {},
    }
    return w.defWndProc(hwnd, msg, wp, lp);
}
