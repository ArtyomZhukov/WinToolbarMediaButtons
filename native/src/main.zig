const w    = @import("win32.zig");
const tb   = @import("toolbar_window.zig");
const tray = @import("tray.zig");

var g_running: bool = true;

fn doQuit() void {
    tray.destroy();
    w.postQuit(0);
}

fn isAutostartEnabled() bool {
    return false; // TODO Phase 8: autostart.zig
}

fn toggleAutostart() void {
    // TODO Phase 8: autostart.zig
}

pub fn main() void {
    _ = w.setDpiAwareness(w.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    const hinstance: w.HINSTANCE = @ptrCast(w.getModuleHandle(null));

    const hwnd = tb.create(hinstance) catch w.exit(1);

    // Wire up tray callbacks
    tray.isAutostartEnabled = &isAutostartEnabled;
    tray.toggleAutostart    = &toggleAutostart;
    tray.quitFn             = &doQuit;
    tray.create(hwnd);

    var msg: w.MSG = undefined;
    while (w.getMsg(&msg, null, 0, 0) > 0) {
        _ = w.translate(&msg);
        _ = w.dispatch(&msg);
    }
    w.exit(0);
}
