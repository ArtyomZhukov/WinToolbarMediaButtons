// Windows.UI.Composition via raw COM vtable calls.
// Vtable offsets match exactly the C# version (ToolbarWindow.cs).

const w   = @import("win32.zig");
const std = @import("std");

const HRESULT = w.LONG;

// ── GUIDs ────────────────────────────────────────────────────────────────────

pub const GUID = extern struct { d1: u32, d2: u16, d3: u16, d4: [8]u8 };

pub const IID_IUnknown              = GUID{ .d1=0x00000000,.d2=0x0000,.d3=0x0000,.d4=.{0xC0,0x00,0x00,0x00,0x00,0x00,0x00,0x46} };
pub const IID_IInspectable          = GUID{ .d1=0xAF86E2E0,.d2=0xB12D,.d3=0x4C6A,.d4=.{0x9C,0x5A,0xD7,0xAA,0x65,0x10,0x1E,0x90} };
pub const IID_ICompositor           = GUID{ .d1=0xB403CA50,.d2=0x7F8C,.d3=0x4E83,.d4=.{0x98,0x5F,0xCC,0x45,0x06,0x00,0x36,0xD8} };
pub const IID_ICompositorDesktopInterop = GUID{ .d1=0x29E691FA,.d2=0x4567,.d3=0x4DCA,.d4=.{0xB3,0x19,0xD0,0xF2,0x07,0xEB,0x68,0x07} };
pub const IID_ICompositorInterop    = GUID{ .d1=0x25297D5C,.d2=0x3AD4,.d3=0x4C9C,.d4=.{0xB5,0xCF,0xE3,0x6A,0x38,0x51,0x23,0x30} };
pub const IID_IVisual               = GUID{ .d1=0x117E202D,.d2=0xA859,.d3=0x4C89,.d4=.{0x87,0x3B,0xC2,0xAA,0x56,0x67,0x88,0xE3} };
pub const IID_IVisual2              = GUID{ .d1=0x3052B611,.d2=0x56C3,.d3=0x4C3E,.d4=.{0x8B,0xF3,0xF6,0xE1,0xAD,0x47,0x3F,0x06} };
pub const IID_ISpriteVisual         = GUID{ .d1=0x08E05581,.d2=0x1AD1,.d3=0x4F97,.d4=.{0x97,0x57,0x40,0x2D,0x76,0xE4,0x23,0x3B} };
pub const IID_ICompositionSurfaceBrush = GUID{ .d1=0xAD016D79,.d2=0x1E4C,.d3=0x4C0D,.d4=.{0x9C,0x29,0x83,0x33,0x8C,0x87,0xC1,0x62} };
pub const IID_ICompositionTarget    = GUID{ .d1=0xA1BEA8BA,.d2=0xD726,.d3=0x4663,.d4=.{0x81,0x29,0x6B,0x5E,0x79,0x27,0xFF,0xA6} };

// ── combase dynamic loading ───────────────────────────────────────────────────
// combase.lib is not in Zig's bundled Windows SDK, so we load at runtime.

const FnRoActivateInstance = *const fn (?*anyopaque, *?*anyopaque) callconv(.winapi) HRESULT;
const FnWindowsCreateString = *const fn ([*]const u16, u32, *?*anyopaque) callconv(.winapi) HRESULT;
const FnWindowsDeleteString = *const fn (?*anyopaque) callconv(.winapi) HRESULT;

var g_ro_activate:    ?FnRoActivateInstance  = null;
var g_create_string:  ?FnWindowsCreateString = null;
var g_delete_string:  ?FnWindowsDeleteString = null;

fn ensureCombase() bool {
    if (g_ro_activate != null) return true;
    const lib = w.loadLibrary(std.unicode.utf8ToUtf16LeStringLiteral("combase.dll")) orelse return false;
    g_ro_activate   = @ptrCast(w.getProcAddress(lib, "RoActivateInstance"));
    g_create_string = @ptrCast(w.getProcAddress(lib, "WindowsCreateString"));
    g_delete_string = @ptrCast(w.getProcAddress(lib, "WindowsDeleteString"));
    return g_ro_activate != null and g_create_string != null;
}

// ── vtable helpers ────────────────────────────────────────────────────────────

inline fn vtbl(obj: *anyopaque) [*]const *const anyopaque {
    return @as(*const [*]const *const anyopaque, @ptrCast(@alignCast(obj))).*;
}

pub fn qi(obj: *anyopaque, iid: *const GUID) ?*anyopaque {
    const Fn = *const fn (*anyopaque, *const GUID, *?*anyopaque) callconv(.winapi) HRESULT;
    const f: Fn = @ptrCast(vtbl(obj)[0]); // IUnknown::QueryInterface
    var out: ?*anyopaque = null;
    _ = f(obj, iid, &out);
    return out;
}

pub fn release(obj: *anyopaque) void {
    const Fn = *const fn (*anyopaque) callconv(.winapi) u32;
    const f: Fn = @ptrCast(vtbl(obj)[2]); // IUnknown::Release
    _ = f(obj);
}

// ── Activation ───────────────────────────────────────────────────────────────

pub fn activateCompositor() ?*anyopaque {
    if (!ensureCombase()) return null;
    const createStr  = g_create_string orelse return null;
    const deleteStr  = g_delete_string;
    const roActivate = g_ro_activate   orelse return null;

    const class_name = std.unicode.utf8ToUtf16LeStringLiteral("Windows.UI.Composition.Compositor");
    var hs: ?*anyopaque = null;
    if (createStr(class_name, @intCast(class_name.len), &hs) != 0) return null;
    defer if (deleteStr) |f| { _ = f(hs); };

    // RoActivateInstance gives an IInspectable* with refcount=1
    var raw: ?*anyopaque = null;
    if (roActivate(hs, &raw) != 0) return null;
    const raw_nn = raw orelse return null;
    defer release(raw_nn);

    // Return ICompositor* so vtable offsets [22]/[24] are correct
    return qi(raw_nn, &IID_ICompositor);
}

// ── ICompositorDesktopInterop ─────────────────────────────────────────────────

pub fn createDesktopWindowTarget(
    compositor_raw: *anyopaque,
    hwnd: w.HWND,
    topmost: w.BOOL,
) ?*anyopaque {
    const interop = qi(compositor_raw, &IID_ICompositorDesktopInterop) orelse return null;
    defer release(interop);

    // ICompositorDesktopInterop vtable:
    // [0] QI  [1] AddRef  [2] Release
    // [3] CreateDesktopWindowTarget  [4] EnsureOnThread
    const Fn = *const fn (*anyopaque, w.HWND, w.BOOL, *?*anyopaque) callconv(.winapi) HRESULT;
    const f: Fn = @ptrCast(vtbl(interop)[3]);
    var target: ?*anyopaque = null;
    if (f(interop, hwnd, topmost, &target) != 0) return null;
    return target;
}

// ── ICompositor vtable calls ──────────────────────────────────────────────────

// [22] CreateSpriteVisual
pub fn createSpriteVisual(compositor: *anyopaque) ?*anyopaque {
    const Fn = *const fn (*anyopaque, *?*anyopaque) callconv(.winapi) HRESULT;
    const f: Fn = @ptrCast(vtbl(compositor)[22]);
    var out: ?*anyopaque = null;
    if (f(compositor, &out) != 0) return null;
    return out;
}

// [24] CreateSurfaceBrush(surface)
pub fn createSurfaceBrush(compositor: *anyopaque, surface: *anyopaque) ?*anyopaque {
    const Fn = *const fn (*anyopaque, *anyopaque, *?*anyopaque) callconv(.winapi) HRESULT;
    const f: Fn = @ptrCast(vtbl(compositor)[24]);
    var out: ?*anyopaque = null;
    if (f(compositor, surface, &out) != 0) return null;
    return out;
}

// ── IVisual2 [11] set_RelativeSizeAdjustment(Vector2) ────────────────────────

const Vector2 = extern struct { x: f32, y: f32 };

pub fn vis2SetRelativeSize(spr: *anyopaque) void {
    const vis2 = qi(spr, &IID_IVisual2) orelse return;
    defer release(vis2);
    const Fn = *const fn (*anyopaque, Vector2) callconv(.winapi) HRESULT;
    const f: Fn = @ptrCast(vtbl(vis2)[11]);
    _ = f(vis2, .{ .x = 1.0, .y = 1.0 });
}

// ── ICompositionTarget [7] set_Root(IVisual*) ─────────────────────────────────

pub fn targetSetRoot(target_raw: *anyopaque, spr: *anyopaque) void {
    const target = qi(target_raw, &IID_ICompositionTarget) orelse return;
    defer release(target);
    const vis = qi(spr, &IID_IVisual) orelse return;
    defer release(vis);
    const Fn = *const fn (*anyopaque, *anyopaque) callconv(.winapi) HRESULT;
    const f: Fn = @ptrCast(vtbl(target)[7]);
    _ = f(target, vis);
}

// ── ISpriteVisual [7] set_Brush(IInspectable*) ───────────────────────────────

pub fn spriteSetBrush(spr: *anyopaque, brush: *anyopaque) void {
    const sv = qi(spr, &IID_ISpriteVisual) orelse return;
    defer release(sv);
    const bi = qi(brush, &IID_IInspectable) orelse return;
    defer release(bi);
    const Fn = *const fn (*anyopaque, *anyopaque) callconv(.winapi) HRESULT;
    const f: Fn = @ptrCast(vtbl(sv)[7]);
    _ = f(sv, bi);
}

// ── ICompositionSurfaceBrush [11] set_Stretch(int) Fill=2 ────────────────────

pub fn surfaceBrushSetStretch(brush: *anyopaque) void {
    const sb = qi(brush, &IID_ICompositionSurfaceBrush) orelse return;
    defer release(sb);
    const Fn = *const fn (*anyopaque, w.INT) callconv(.winapi) HRESULT;
    const f: Fn = @ptrCast(vtbl(sb)[11]);
    _ = f(sb, 2); // Fill
}

// ── ICompositorInterop::CreateCompositionSurfaceForSwapChain ─────────────────

pub fn createCompositionSurfaceForSwapChain(
    compositor_raw: *anyopaque,
    swap_chain: *anyopaque,
) ?*anyopaque {
    const interop = qi(compositor_raw, &IID_ICompositorInterop) orelse return null;
    defer release(interop);

    // ICompositorInterop vtable:
    // [0] QI  [1] AddRef  [2] Release
    // [3] CreateCompositionSurfaceForHandle
    // [4] CreateCompositionSurfaceForSwapChain
    // [5] CreateGraphicsDevice
    const Fn = *const fn (*anyopaque, *anyopaque, *?*anyopaque) callconv(.winapi) HRESULT;
    const f: Fn = @ptrCast(vtbl(interop)[4]);
    var out: ?*anyopaque = null;
    if (f(interop, swap_chain, &out) != 0) return null;
    return out;
}
