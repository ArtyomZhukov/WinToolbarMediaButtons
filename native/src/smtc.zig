// Polling-based playback state via Windows.Media.Control SMTC (WinRT COM).
// Loads combase.dll dynamically; gracefully does nothing if unavailable.
// Works for SMTC-registered players (Spotify, VLC 3+, WMP, etc.).
// Browsers that don't publish SMTC sessions (e.g. Yandex Browser) fall
// back to the peak-meter path in toolbar_window.zig.

const w    = @import("win32.zig");
const comp = @import("composition.zig");
const std  = @import("std");

const GUID    = comp.GUID;
const HRESULT = w.LONG;

const IID_IActivationFactory = GUID{
    .d1=0x00000035, .d2=0x0000, .d3=0x0000,
    .d4=.{0xC0,0x00,0x00,0x00,0x00,0x00,0x00,0x46}
};
// IGlobalSystemMediaTransportControlsSessionManagerStatics
// IID verified via GetIids on the activation factory
const IID_SMTCStatics = GUID{
    .d1=0x2050C4EE, .d2=0x11A0, .d3=0x57DE,
    .d4=.{0xAE,0xD7,0xC9,0x7C,0x70,0x33,0x82,0x45}
};

inline fn vt(obj: *anyopaque) [*]const *const anyopaque {
    return @as(*const [*]const *const anyopaque, @ptrCast(@alignCast(obj))).*;
}
inline fn rel(obj: *anyopaque) void {
    _ = @as(*const fn (*anyopaque) callconv(.winapi) u32, @ptrCast(vt(obj)[2]))(obj);
}

var g_async_op : ?*anyopaque = null;  // IAsyncOperation<SessionManager>*
var g_mgr      : ?*anyopaque = null;  // IGlobalSystemMediaTransportControlsSessionManager*
var g_init_done: bool = false;

pub fn init() void {
    if (g_init_done) return;
    g_init_done = true;

    const lib = w.loadLibrary(std.unicode.utf8ToUtf16LeStringLiteral("combase.dll")) orelse return;
    const p_init    = w.getProcAddress(lib, "RoInitialize")          orelse return;
    const p_mkstr   = w.getProcAddress(lib, "WindowsCreateString")    orelse return;
    const p_delstr  = w.getProcAddress(lib, "WindowsDeleteString")    orelse return;
    const p_factory = w.getProcAddress(lib, "RoGetActivationFactory") orelse return;

    _ = @as(*const fn (i32) callconv(.winapi) HRESULT, @ptrCast(p_init))(0);

    const cname = std.unicode.utf8ToUtf16LeStringLiteral(
        "Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager");
    var hs: ?*anyopaque = null;
    if (@as(*const fn ([*]const u16, u32, *?*anyopaque) callconv(.winapi) HRESULT,
            @ptrCast(p_mkstr))(@as([*]const u16, cname), @intCast(cname.len), &hs) != 0) return;
    defer _ = @as(*const fn (?*anyopaque) callconv(.winapi) HRESULT, @ptrCast(p_delstr))(hs);

    // Get base IActivationFactory, then QI for the specific statics interface
    var base_raw: ?*anyopaque = null;
    if (@as(*const fn (?*anyopaque, *const GUID, *?*anyopaque) callconv(.winapi) HRESULT,
            @ptrCast(p_factory))(hs, &IID_IActivationFactory, &base_raw) != 0) return;
    const base = base_raw orelse return;
    defer rel(base);

    var statics: ?*anyopaque = null;
    if (@as(*const fn (*anyopaque, *const GUID, *?*anyopaque) callconv(.winapi) HRESULT,
            @ptrCast(vt(base)[0]))(base, &IID_SMTCStatics, &statics) != 0) return;
    const fact = statics orelse return;
    defer rel(fact);

    // IGlobalSystemMediaTransportControlsSessionManagerStatics::RequestAsync [vtable[6]]
    var op: ?*anyopaque = null;
    if (@as(*const fn (*anyopaque, *?*anyopaque) callconv(.winapi) HRESULT,
            @ptrCast(vt(fact)[6]))(fact, &op) != 0) return;
    g_async_op = op;
}

pub fn poll() void {
    if (g_mgr != null) return;
    const op = g_async_op orelse return;

    // GetResults [vtable[8]] returns E_ILLEGAL_METHOD_CALL while pending, S_OK when done
    var result: ?*anyopaque = null;
    const hr = @as(*const fn (*anyopaque, *?*anyopaque) callconv(.winapi) HRESULT,
        @ptrCast(vt(op)[8]))(op, &result);
    if (hr == 0 and result != null) {
        g_mgr = result;
        rel(op);
        g_async_op = null;
    }
}

pub fn isAvailable() bool { return g_mgr != null; }

pub fn isPlaying() bool {
    const mgr = g_mgr orelse return false;

    // IGlobalSystemMediaTransportControlsSessionManager vtable (after IInspectable[3-5]):
    // [6]=GetSessions, [7]=get_CurrentSession, [8]=add_CurrentSessionChanged, [9]=remove_CurrentSessionChanged
    var sessions_ptr: ?*anyopaque = null;
    if (@as(*const fn (*anyopaque, *?*anyopaque) callconv(.winapi) HRESULT,
            @ptrCast(vt(mgr)[6]))(mgr, &sessions_ptr) != 0) return false;
    const sv = sessions_ptr orelse return false;
    defer rel(sv);

    // IVectorView<Session>: [6]=GetAt, [7]=get_Size
    var count: u32 = 0;
    _ = @as(*const fn (*anyopaque, *u32) callconv(.winapi) HRESULT,
        @ptrCast(vt(sv)[7]))(sv, &count);

    var i: u32 = 0;
    while (i < count) : (i += 1) {
        var sess: ?*anyopaque = null;
        if (@as(*const fn (*anyopaque, u32, *?*anyopaque) callconv(.winapi) HRESULT,
                @ptrCast(vt(sv)[6]))(sv, i, &sess) != 0) continue;
        const s = sess orelse continue;
        defer rel(s);

        // IGlobalSystemMediaTransportControlsSession vtable (after IInspectable[3-5]):
        // [6]=get_SourceAppUserModelId [7]=TryGetMediaPropertiesAsync
        // [8]=GetTimelineProperties    [9]=GetPlaybackInfo
        var info: ?*anyopaque = null;
        if (@as(*const fn (*anyopaque, *?*anyopaque) callconv(.winapi) HRESULT,
                @ptrCast(vt(s)[9]))(s, &info) != 0) continue;
        const pi = info orelse continue;
        defer rel(pi);

        // IGlobalSystemMediaTransportControlsSessionPlaybackInfo vtable (after IInspectable[3-5]):
        // [6]=GetControls [7]=IsShuffleActive [8]=PlaybackRate [9]=PlaybackType
        // [10]=AutoRepeatMode [11]=get_PlaybackStatus
        // MediaPlaybackStatus: 0=Closed 1=Opened 2=Changing 3=Stopped 4=Playing 5=Paused
        var ps: i32 = 0;
        _ = @as(*const fn (*anyopaque, *i32) callconv(.winapi) HRESULT,
            @ptrCast(vt(pi)[11]))(pi, &ps);
        if (ps == 4) return true;
    }
    return false;
}
