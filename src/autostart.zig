const w = @import("win32.zig");

const RUN_KEY  = w.L("Software\\Microsoft\\Windows\\CurrentVersion\\Run");
const APP_NAME = w.L("WinToolbarMediaButtons");

pub fn isEnabled() bool {
    var hkey: w.HKEY = null;
    if (w.RegOpenKeyExW(w.HKEY_CURRENT_USER, RUN_KEY, 0, w.KEY_QUERY_VALUE, &hkey) != 0)
        return false;
    defer _ = w.RegCloseKey(hkey);
    var sz: w.DWORD = 0;
    return w.RegQueryValueExW(hkey, APP_NAME, null, null, null, &sz) == 0;
}

pub fn toggle() void {
    if (isEnabled()) disable() else enable();
}

fn enable() void {
    var path: [1024]w.WCHAR = undefined;
    const len = w.getModuleFileName(null, &path, path.len);
    if (len == 0) return;
    var hkey: w.HKEY = null;
    if (w.RegOpenKeyExW(w.HKEY_CURRENT_USER, RUN_KEY, 0, w.KEY_SET_VALUE, &hkey) != 0)
        return;
    defer _ = w.RegCloseKey(hkey);
    _ = w.RegSetValueExW(hkey, APP_NAME, 0, w.REG_SZ, @ptrCast(&path), (len + 1) * 2);
}

fn disable() void {
    var hkey: w.HKEY = null;
    if (w.RegOpenKeyExW(w.HKEY_CURRENT_USER, RUN_KEY, 0, w.KEY_SET_VALUE, &hkey) != 0)
        return;
    defer _ = w.RegCloseKey(hkey);
    _ = w.RegDeleteValueW(hkey, APP_NAME);
}
