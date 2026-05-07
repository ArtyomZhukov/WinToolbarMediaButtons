const w    = @import("win32.zig");
const comp = @import("composition.zig");
const rend = @import("renderer.zig");
const audio = @import("audio.zig");

const vt  = comp.vtbl;
const qi  = comp.qi;
const rel = comp.release;

const CLASS_NAME = w.L("WTMB");

var g_btn_size  : w.INT = 32;
var g_margin_l  : w.INT = 0;
var g_dragging : bool = false;

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
    const margin_r: w.INT  = @max(2, @divTrunc(8 * dpi, 96));
    const margin_l: w.INT  = margin_r;
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

    const compositor = blk: {
        const cb_lib = w.loadLibrary("combase.dll") orelse w.exit(1);
        const FnRoAct = *const fn (?*anyopaque, *?*anyopaque) callconv(.winapi) w.LONG;
        const FnMkStr = *const fn ([*]const u16, u32, *?*anyopaque) callconv(.winapi) w.LONG;
        const roActivate: FnRoAct = @ptrCast(w.getProcAddress(cb_lib, "RoActivateInstance") orelse w.exit(1));
        const createStr:  FnMkStr = @ptrCast(w.getProcAddress(cb_lib, "WindowsCreateString") orelse w.exit(1));
        const class_name = w.L("Windows.UI.Composition.Compositor");
        var hs: ?*anyopaque = null;
        if (createStr(class_name, @intCast(class_name.len), &hs) != 0) w.exit(1);
        var raw: ?*anyopaque = null;
        if (roActivate(hs, &raw) != 0) w.exit(1);
        const raw_nn = raw orelse w.exit(1);
        defer rel(raw_nn);
        break :blk qi(raw_nn, &comp.IID_ICompositor) orelse w.exit(1);
    };

    const target = blk: {
        const interop = qi(compositor, &comp.IID_ICompositorDesktopInterop) orelse w.exit(1);
        defer rel(interop);
        var t: ?*anyopaque = null;
        if (@as(*const fn (*anyopaque, w.HWND, w.BOOL, *?*anyopaque) callconv(.winapi) w.LONG,
                @ptrCast(vt(interop)[3]))(interop, hwnd, 1, &t) != 0) w.exit(1);
        break :blk t orelse w.exit(1);
    };

    var spr_raw: ?*anyopaque = null;
    if (@as(*const fn (*anyopaque, *?*anyopaque) callconv(.winapi) w.LONG,
            @ptrCast(vt(compositor)[22]))(compositor, &spr_raw) != 0) w.exit(1);
    const spr = spr_raw orelse w.exit(1);

    if (qi(spr, &comp.IID_IVisual2)) |vis2| {
        defer rel(vis2);
        const Vector2 = extern struct { x: f32, y: f32 };
        _ = @as(*const fn (*anyopaque, Vector2) callconv(.winapi) w.LONG,
            @ptrCast(vt(vis2)[11]))(vis2, .{ .x = 1.0, .y = 1.0 });
    }
    if (qi(target, &comp.IID_ICompositionTarget)) |tgt| {
        defer rel(tgt);
        if (qi(spr, &comp.IID_IVisual)) |vis| {
            defer rel(vis);
            _ = @as(*const fn (*anyopaque, *anyopaque) callconv(.winapi) w.LONG,
                @ptrCast(vt(tgt)[7]))(tgt, vis);
        }
    }

    _ = w.setParent(hwnd, taskbar);
    const style_u: w.DWORD = @bitCast(w.getWindowLong(hwnd, w.GWL_STYLE));
    const new_style: w.LONG = @bitCast(
        (style_u & ~w.WS_POPUP) | w.WS_CHILD | w.WS_VISIBLE | w.WS_CLIPCHILDREN | w.WS_CLIPSIBLINGS
    );
    _ = w.setWindowLong(hwnd, w.GWL_STYLE, new_style);

    _ = w.setWindowPos(hwnd, null, 0, 0, toolbar_w_phys, actual_h,
        w.SWP_NOACTIVATE | w.SWP_FRAMECHANGED);

    audio.init();
    rend.init(compositor, spr, @intCast(toolbar_w_phys), @intCast(actual_h), @intCast(btn_size), @intCast(margin_l));
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
            _ = rend.setPlaying(audio.getPeak() > 0.001);
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
