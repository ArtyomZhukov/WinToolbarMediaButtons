const w  = @import("win32.zig");
const tb = @import("toolbar_window.zig");

pub export fn wWinMainCRTStartup() callconv(.winapi) noreturn {
    _ = w.setDpiAwareness(w.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    const hinstance: w.HINSTANCE = @ptrCast(w.getModuleHandle(null));
    tb.create(hinstance);
    var msg: w.MSG = undefined;
    while (w.getMsg(&msg, null, 0, 0) > 0) {
        _ = w.dispatch(&msg);
    }
    w.exit(0);
}
