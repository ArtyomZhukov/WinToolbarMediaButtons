const w         = @import("win32.zig");
const tb        = @import("toolbar_window.zig");
const tray      = @import("tray.zig");
const autostart = @import("autostart.zig");

fn doQuit() void {
    tray.destroy();
    w.postQuit(0);
}

pub export fn wWinMainCRTStartup() callconv(.winapi) noreturn {
    w.initWin32();
    _ = w.setDpiAwareness(w.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    const hinstance: w.HINSTANCE = @ptrCast(w.getModuleHandle(null));

    const hwnd = tb.create(hinstance) catch w.exit(1);

    tray.isAutostartEnabled = &autostart.isEnabled;
    tray.toggleAutostart    = &autostart.toggle;
    tray.quitFn             = &doQuit;
    tray.create(hwnd);

    var msg: w.MSG = undefined;
    while (w.getMsg(&msg, null, 0, 0) > 0) {
        _ = w.translate(&msg);
        _ = w.dispatch(&msg);
    }
    w.exit(0);
}
