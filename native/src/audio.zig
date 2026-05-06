// WASAPI volume + peak meter via dynamic COM loading.
// Lazy-initialises on first use.

const w    = @import("win32.zig");
const comp = @import("composition.zig");
const std  = @import("std");

const GUID    = comp.GUID;
const HRESULT = w.LONG;

const CLSID_MMDeviceEnumerator = GUID{ .d1=0xBCDE0395,.d2=0xE52F,.d3=0x467C,.d4=.{0x8E,0x3D,0xC4,0x57,0x92,0x91,0x69,0x2E} };
const IID_IMMDeviceEnumerator  = GUID{ .d1=0xA95664D2,.d2=0x9614,.d3=0x4F35,.d4=.{0xA7,0x46,0xDE,0x8D,0xB6,0x36,0x17,0xE6} };
const IID_IAudioEndpointVolume = GUID{ .d1=0x5CDF2C82,.d2=0x841E,.d3=0x4546,.d4=.{0x97,0x22,0x0C,0xF7,0x40,0x78,0x22,0x9A} };
const IID_IAudioMeterInfo      = GUID{ .d1=0xC02216F6,.d2=0x8C67,.d3=0x4B5B,.d4=.{0x9D,0x00,0xD0,0x08,0xE7,0x3E,0x00,0x64} };
const CLSCTX_ALL: u32 = 0x17;

inline fn vt(obj: *anyopaque) [*]const *const anyopaque {
    return @as(*const [*]const *const anyopaque, @ptrCast(@alignCast(obj))).*;
}
inline fn rel(obj: *anyopaque) void {
    _ = @as(*const fn (*anyopaque) callconv(.winapi) u32, @ptrCast(vt(obj)[2]))(obj);
}

var g_vol     : ?*anyopaque = null;  // IAudioEndpointVolume*
var g_meter   : ?*anyopaque = null;  // IAudioMeterInformation*

fn ensureVol() bool {
    if (g_vol != null) return true;
    const lib = w.loadLibrary(std.unicode.utf8ToUtf16LeStringLiteral("ole32.dll")) orelse return false;
    const proc = w.getProcAddress(lib, "CoCreateInstance") orelse return false;
    const FnCoCreate = *const fn (*const GUID, ?*anyopaque, u32, *const GUID, *?*anyopaque) callconv(.winapi) HRESULT;

    var en_raw: ?*anyopaque = null;
    if (@as(FnCoCreate, @ptrCast(proc))(&CLSID_MMDeviceEnumerator, null, CLSCTX_ALL, &IID_IMMDeviceEnumerator, &en_raw) != 0) return false;
    const en = en_raw orelse return false;
    defer rel(en);

    const FnGetDef = *const fn (*anyopaque, i32, i32, *?*anyopaque) callconv(.winapi) HRESULT;
    var dev_raw: ?*anyopaque = null;
    if (@as(FnGetDef, @ptrCast(vt(en)[4]))(en, 0, 1, &dev_raw) != 0) return false;
    const dev = dev_raw orelse return false;
    defer rel(dev);

    const FnActivate = *const fn (*anyopaque, *const GUID, u32, ?*anyopaque, *?*anyopaque) callconv(.winapi) HRESULT;

    var vol_raw: ?*anyopaque = null;
    if (@as(FnActivate, @ptrCast(vt(dev)[3]))(dev, &IID_IAudioEndpointVolume, CLSCTX_ALL, null, &vol_raw) != 0) return false;
    g_vol = vol_raw;

    // Also grab IAudioMeterInformation while we hold the device
    var meter_raw: ?*anyopaque = null;
    _ = @as(FnActivate, @ptrCast(vt(dev)[3]))(dev, &IID_IAudioMeterInfo, CLSCTX_ALL, null, &meter_raw);
    g_meter = meter_raw;

    return true;
}

fn getVol() ?*anyopaque {
    if (g_vol == null) _ = ensureVol();
    return g_vol;
}

// IAudioEndpointVolume vtable:
//  [7]  SetMasterVolumeLevelScalar(f32, *GUID)
//  [9]  GetMasterVolumeLevelScalar(*f32)
//  [14] SetMute(BOOL, *GUID)
//  [15] GetMute(*BOOL)

pub fn getVolume() f32 {
    const vol = getVol() orelse return 0;
    var level: f32 = 0;
    _ = @as(*const fn (*anyopaque, *f32) callconv(.winapi) HRESULT,
        @ptrCast(vt(vol)[9]))(vol, &level);
    return level;
}

pub fn setVolume(level: f32) void {
    const vol = getVol() orelse return;
    const clamped = @max(0.0, @min(1.0, level));
    _ = @as(*const fn (*anyopaque, f32, ?*anyopaque) callconv(.winapi) HRESULT,
        @ptrCast(vt(vol)[7]))(vol, clamped, null);
}

pub fn getMute() bool {
    const vol = getVol() orelse return false;
    var muted: i32 = 0;
    _ = @as(*const fn (*anyopaque, *i32) callconv(.winapi) HRESULT,
        @ptrCast(vt(vol)[15]))(vol, &muted);
    return muted != 0;
}

pub fn toggleMute() void {
    const vol = getVol() orelse return;
    var muted: i32 = 0;
    _ = @as(*const fn (*anyopaque, *i32) callconv(.winapi) HRESULT,
        @ptrCast(vt(vol)[15]))(vol, &muted);
    _ = @as(*const fn (*anyopaque, i32, ?*anyopaque) callconv(.winapi) HRESULT,
        @ptrCast(vt(vol)[14]))(vol, 1 - muted, null);
}


// IAudioMeterInformation::GetPeak [vtable[3]] → peak amplitude 0.0–1.0.
// Returns 0 if meter not available.
pub fn getPeak() f32 {
    if (g_meter == null) _ = ensureVol();
    const m = g_meter orelse return 0;
    var peak: f32 = 0;
    _ = @as(*const fn (*anyopaque, *f32) callconv(.winapi) HRESULT,
        @ptrCast(vt(m)[3]))(m, &peak);
    return peak;
}
