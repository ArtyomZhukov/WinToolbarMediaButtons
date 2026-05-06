// Phase 5: D3D11 + DXGI swap chain + D2D1 DeviceContext, wired into Composition.
// All DLLs loaded dynamically (d3d11/d2d1 not in Zig bundled SDK).

const w     = @import("win32.zig");
const comp  = @import("composition.zig");
const audio = @import("audio.zig");
const std   = @import("std");

pub const HitZone = enum { none, prev, play, next, slider, vol };

// ── Constants ─────────────────────────────────────────────────────────────────

const DXGI_FORMAT_B8G8R8A8_UNORM    : i32 = 87;
const DXGI_USAGE_RENDER_TARGET_OUTPUT: u32 = 0x20;
const DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL: i32 = 3;
const DXGI_ALPHA_MODE_PREMULTIPLIED : i32 = 1;
const DXGI_SCALING_STRETCH          : i32 = 0;
const D3D11_SDK_VERSION             : u32 = 7;
const D3D_DRIVER_TYPE_HARDWARE      : i32 = 1;
const D3D_DRIVER_TYPE_WARP          : i32 = 5;
const D3D11_CREATE_DEVICE_BGRA_SUPPORT: u32 = 0x20;

// ── GUIDs ─────────────────────────────────────────────────────────────────────

const GUID = comp.GUID;
const IID_IDXGIDevice   = GUID{ .d1=0x54ec77fa,.d2=0x1377,.d3=0x44e6,.d4=.{0x8c,0x32,0x88,0xfd,0x5f,0x44,0xc8,0x4c} };
const IID_IDXGIFactory2 = GUID{ .d1=0x50c83a1c,.d2=0xe072,.d3=0x4c48,.d4=.{0x87,0xb0,0x36,0x30,0xfa,0x36,0xa6,0xd0} };
const IID_IDXGISurface  = GUID{ .d1=0xcafcb56c,.d2=0x6ac3,.d3=0x4889,.d4=.{0xbf,0x47,0x9e,0x23,0xbb,0xd2,0x60,0xec} };
const IID_ID2D1Factory1 = GUID{ .d1=0xbb12d362,.d2=0xdaee,.d3=0x4b9a,.d4=.{0xaa,0x1d,0x14,0xba,0x40,0x1c,0xfa,0x1f} };
const IID_IDWriteFactory = GUID{ .d1=0xb859ee5a,.d2=0xd838,.d3=0x4b5b,.d4=.{0xa2,0xe8,0x1a,0xdc,0x7d,0x93,0xdb,0x48} };

// Segoe MDL2 Assets glyphs (UTF-16)
const ICON_PREV  = [1]u16{0xE892};
const ICON_PLAY  = [1]u16{0xE102};
const ICON_PAUSE = [1]u16{0xE103};
const ICON_NEXT  = [1]u16{0xE893};
const ICON_VOL   = [1]u16{0xE767};
const ICON_MUTE  = [1]u16{0xE74F};
const ICON_VOL0  = [1]u16{0xE992};  // speaker, no waves
const ICON_VOL1  = [1]u16{0xE993};  // one wave
const ICON_VOL2  = [1]u16{0xE767};  // two waves (same as VOL)

// ── Structs ───────────────────────────────────────────────────────────────────

const SwapChainDesc = extern struct {
    Width: u32, Height: u32,
    Format: i32, Stereo: i32,
    SampleCount: u32, SampleQuality: u32,
    BufferUsage: u32, BufferCount: u32,
    Scaling: i32, SwapEffect: i32, AlphaMode: i32,
    Flags: u32,
};

pub const ColorF = extern struct { r: f32, g: f32, b: f32, a: f32 };

const BitmapProperties1 = extern struct {
    format:        i32,  // DXGI_FORMAT
    alphaMode:     i32,
    dpiX:          f32,
    dpiY:          f32,
    bitmapOptions: i32,
    _pad:          i32,
    colorContext:  ?*anyopaque,
};

pub const RectF = extern struct { left: f32, top: f32, right: f32, bottom: f32 };
pub const RoundedRect = extern struct { rect: RectF, radiusX: f32, radiusY: f32 };

// ── Dynamic loading ───────────────────────────────────────────────────────────

const FnD3D11 = *const fn (?*anyopaque, i32, ?*anyopaque, u32, ?*anyopaque, u32, u32,
    *?*anyopaque, ?*anyopaque, ?*anyopaque) callconv(.winapi) w.LONG;
const FnD2D1  = *const fn (i32, *const GUID, ?*anyopaque, *?*anyopaque) callconv(.winapi) w.LONG;

var fn_d3d11: ?FnD3D11 = null;
var fn_d2d1:  ?FnD2D1  = null;

fn loadLibs() bool {
    if (fn_d3d11 != null) return true;
    const l3 = w.loadLibrary(std.unicode.utf8ToUtf16LeStringLiteral("d3d11.dll")) orelse return false;
    const l2 = w.loadLibrary(std.unicode.utf8ToUtf16LeStringLiteral("d2d1.dll"))  orelse return false;
    fn_d3d11 = @ptrCast(w.getProcAddress(l3, "D3D11CreateDevice"));
    fn_d2d1  = @ptrCast(w.getProcAddress(l2, "D2D1CreateFactory"));
    return fn_d3d11 != null and fn_d2d1 != null;
}

fn initDWrite(phys_h: f32) void {
    const lib = w.loadLibrary(std.unicode.utf8ToUtf16LeStringLiteral("dwrite.dll")) orelse return;
    const proc = w.getProcAddress(lib, "DWriteCreateFactory") orelse return;
    const FnCreate = *const fn (i32, *const GUID, *?*anyopaque) callconv(.winapi) w.LONG;
    var raw: ?*anyopaque = null;
    if (@as(FnCreate, @ptrCast(proc))(0, &IID_IDWriteFactory, &raw) != 0) return;
    const factory = raw orelse return;

    const locale  = std.unicode.utf8ToUtf16LeStringLiteral("en-us");
    const FnFmt   = *const fn (*anyopaque, [*:0]const u16, ?*anyopaque, i32, i32, i32, f32, [*:0]const u16, *?*anyopaque) callconv(.winapi) w.LONG;
    const FnAlign = *const fn (*anyopaque, i32) callconv(.winapi) w.LONG;

    // Icon format: Segoe MDL2 Assets, CENTER/CENTER
    var fmt: ?*anyopaque = null;
    if (@as(FnFmt, @ptrCast(vt(factory)[15]))(factory,
            std.unicode.utf8ToUtf16LeStringLiteral("Segoe MDL2 Assets"),
            null, 400, 0, 5, phys_h * 0.52, locale, &fmt) == 0) {
        const tf = fmt orelse return;
        _ = @as(FnAlign, @ptrCast(vt(tf)[3]))(tf, 2);
        _ = @as(FnAlign, @ptrCast(vt(tf)[4]))(tf, 2);
        g_dw_fmt = tf;
    }

    // Text format: Segoe UI SemiBold, CENTER/CENTER
    var fmt2: ?*anyopaque = null;
    if (@as(FnFmt, @ptrCast(vt(factory)[15]))(factory,
            std.unicode.utf8ToUtf16LeStringLiteral("Segoe UI"),
            null, 600, 0, 5, phys_h * 0.30, locale, &fmt2) == 0) {
        const tf2 = fmt2 orelse return;
        _ = @as(FnAlign, @ptrCast(vt(tf2)[3]))(tf2, 2);
        _ = @as(FnAlign, @ptrCast(vt(tf2)[4]))(tf2, 2);
        g_txt_fmt = tf2;
    }
}

// ── vtable shorthands ─────────────────────────────────────────────────────────

inline fn vt(obj: *anyopaque) [*]const *const anyopaque {
    return @as(*const [*]const *const anyopaque, @ptrCast(@alignCast(obj))).*;
}
inline fn qi(obj: *anyopaque, iid: *const GUID) ?*anyopaque {
    var out: ?*anyopaque = null;
    _ = @as(*const fn (*anyopaque, *const GUID, *?*anyopaque) callconv(.winapi) w.LONG,
        @ptrCast(vt(obj)[0]))(obj, iid, &out);
    return out;
}
inline fn rel(obj: *anyopaque) void {
    _ = @as(*const fn (*anyopaque) callconv(.winapi) u32, @ptrCast(vt(obj)[2]))(obj);
}

// ── State ─────────────────────────────────────────────────────────────────────

var g_sc       : ?*anyopaque = null;   // IDXGISwapChain1*
var g_ctx      : ?*anyopaque = null;   // ID2D1DeviceContext*
var g_w        : u32 = 0;
var g_h        : u32 = 0;
var g_btn      : u32 = 0;             // physical button side length
var g_gap      : u32 = 2;             // physical gap between button slots
var g_ml       : u32 = 0;             // physical left margin
var g_hover    : HitZone = .none;
var g_dw_fmt   : ?*anyopaque = null;   // IDWriteTextFormat* for icons
var g_txt_fmt  : ?*anyopaque = null;   // IDWriteTextFormat* for percentage text
var g_playing  : bool = false;

pub fn setHover(z: HitZone) bool {
    if (g_hover == z) return false;
    g_hover = z;
    return true;
}

pub fn togglePlaying() void { g_playing = !g_playing; }
pub fn setPlaying(p: bool) bool {
    if (g_playing == p) return false;
    g_playing = p;
    return true;
}

// ── Init ──────────────────────────────────────────────────────────────────────

pub fn init(compositor: *anyopaque, root_vis: *anyopaque, width: u32, height: u32, btn_size: u32, gap: u32, margin_l: u32) bool {
    if (!loadLibs()) return false;

    // D3D11 device (hardware, fallback WARP)
    var d3d: ?*anyopaque = null;
    if (fn_d3d11.?(null, D3D_DRIVER_TYPE_HARDWARE, null, D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            null, 0, D3D11_SDK_VERSION, &d3d, null, null) != 0)
        _ = fn_d3d11.?(null, D3D_DRIVER_TYPE_WARP, null, D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            null, 0, D3D11_SDK_VERSION, &d3d, null, null);
    const d3d_dev = d3d orelse return false;
    defer rel(d3d_dev);

    const dxgi_dev = qi(d3d_dev, &IID_IDXGIDevice) orelse return false;
    defer rel(dxgi_dev);

    // D2D1Factory1 → D2D1Device → D2D1DeviceContext
    var fptr: ?*anyopaque = null;
    if (fn_d2d1.?(0, &IID_ID2D1Factory1, null, &fptr) != 0) return false;
    const fact = fptr orelse return false;
    defer rel(fact);

    var d2d_dev: ?*anyopaque = null;
    if (@as(*const fn (*anyopaque, *anyopaque, *?*anyopaque) callconv(.winapi) w.LONG,
            @ptrCast(vt(fact)[17]))(fact, dxgi_dev, &d2d_dev) != 0) return false;
    const ddev = d2d_dev orelse return false;
    defer rel(ddev);

    var ctx: ?*anyopaque = null;
    if (@as(*const fn (*anyopaque, i32, *?*anyopaque) callconv(.winapi) w.LONG,
            @ptrCast(vt(ddev)[4]))(ddev, 0, &ctx) != 0) return false;
    const d2d_ctx = ctx orelse return false;

    // IDXGIDevice → IDXGIAdapter → IDXGIFactory2
    var adp: ?*anyopaque = null;
    if (@as(*const fn (*anyopaque, *?*anyopaque) callconv(.winapi) w.LONG,
            @ptrCast(vt(dxgi_dev)[7]))(dxgi_dev, &adp) != 0) { rel(d2d_ctx); return false; }
    const adapter = adp orelse { rel(d2d_ctx); return false; };
    defer rel(adapter);

    var f2: ?*anyopaque = null;
    if (@as(*const fn (*anyopaque, *const GUID, *?*anyopaque) callconv(.winapi) w.LONG,
            @ptrCast(vt(adapter)[6]))(adapter, &IID_IDXGIFactory2, &f2) != 0) { rel(d2d_ctx); return false; }
    const factory2 = f2 orelse { rel(d2d_ctx); return false; };
    defer rel(factory2);

    // Swap chain for composition (BGRA, premultiplied alpha, flip)
    const desc = SwapChainDesc{
        .Width = width, .Height = height,
        .Format = DXGI_FORMAT_B8G8R8A8_UNORM,
        .Stereo = 0, .SampleCount = 1, .SampleQuality = 0,
        .BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT, .BufferCount = 2,
        .Scaling = DXGI_SCALING_STRETCH, .SwapEffect = DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL,
        .AlphaMode = DXGI_ALPHA_MODE_PREMULTIPLIED, .Flags = 0,
    };
    var sc: ?*anyopaque = null;
    if (@as(*const fn (*anyopaque, *anyopaque, *const SwapChainDesc, ?*anyopaque, *?*anyopaque) callconv(.winapi) w.LONG,
            @ptrCast(vt(factory2)[24]))(factory2, dxgi_dev, &desc, null, &sc) != 0) { rel(d2d_ctx); return false; }
    const swap_chain = sc orelse { rel(d2d_ctx); return false; };

    // Wire swap chain → Composition SpriteVisual brush
    const csurf = comp.createCompositionSurfaceForSwapChain(compositor, swap_chain) orelse {
        rel(d2d_ctx); rel(swap_chain); return false;
    };
    defer comp.release(csurf);

    const brush = comp.createSurfaceBrush(compositor, csurf) orelse {
        rel(d2d_ctx); rel(swap_chain); return false;
    };
    comp.surfaceBrushSetStretch(brush);
    comp.spriteSetBrush(root_vis, brush);
    comp.release(brush);

    g_sc  = swap_chain;
    g_ctx = d2d_ctx;
    g_w   = width;
    g_h   = height;
    g_btn = btn_size;
    g_gap = gap;
    g_ml  = margin_l;
    initDWrite(@floatFromInt(btn_size));
    return true;
}

// ── D2D1 draw helpers ─────────────────────────────────────────────────────────

// vtable indices on ID2D1DeviceContext (confirmed against d2d1.h):
//  [8]  CreateSolidColorBrush
//  [17] FillRectangle
//  [18] DrawRoundedRectangle
//  [19] FillRoundedRectangle
//  [47] Clear
//  [48] BeginDraw
//  [49] EndDraw
//  [62] CreateBitmapFromDxgiSurface
//  [74] SetTarget

fn createBrush(ctx: *anyopaque, color: ColorF) ?*anyopaque {
    var out: ?*anyopaque = null;
    _ = @as(*const fn (*anyopaque, *const ColorF, ?*anyopaque, *?*anyopaque) callconv(.winapi) w.LONG,
        @ptrCast(vt(ctx)[8]))(ctx, &color, null, &out);
    return out;
}

pub fn fillRect(ctx: *anyopaque, r: RectF, brush: *anyopaque) void {
    @as(*const fn (*anyopaque, *const RectF, *anyopaque) callconv(.winapi) void,
        @ptrCast(vt(ctx)[17]))(ctx, &r, brush);
}

pub fn fillRoundedRect(ctx: *anyopaque, rr: RoundedRect, brush: *anyopaque) void {
    @as(*const fn (*anyopaque, *const RoundedRect, *anyopaque) callconv(.winapi) void,
        @ptrCast(vt(ctx)[19]))(ctx, &rr, brush);
}

fn drawIcon(ctx: *anyopaque, icon: []const u16, rect: RectF, brush: *anyopaque) void {
    const fmt = g_dw_fmt orelse return;
    const Fn = *const fn (*anyopaque, [*]const u16, u32, *anyopaque, *const RectF, *anyopaque, u32, i32) callconv(.winapi) void;
    @as(Fn, @ptrCast(vt(ctx)[27]))(ctx, icon.ptr, @intCast(icon.len), fmt, &rect, brush, 0, 0);
}

fn drawText(ctx: *anyopaque, text: []const u16, rect: RectF, brush: *anyopaque) void {
    const fmt = g_txt_fmt orelse return;
    const Fn = *const fn (*anyopaque, [*]const u16, u32, *anyopaque, *const RectF, *anyopaque, u32, i32) callconv(.winapi) void;
    @as(Fn, @ptrCast(vt(ctx)[27]))(ctx, text.ptr, @intCast(text.len), fmt, &rect, brush, 0, 0);
}

// ── Render ─────────────────────────────────────────────────────────────────────

pub fn render() void {
    const sc  = g_sc  orelse return;
    const ctx = g_ctx orelse return;

    // GetBuffer(0, IDXGISurface) [vtable[9]]
    var sp: ?*anyopaque = null;
    if (@as(*const fn (*anyopaque, u32, *const GUID, *?*anyopaque) callconv(.winapi) w.LONG,
            @ptrCast(vt(sc)[9]))(sc, 0, &IID_IDXGISurface, &sp) != 0) return;
    const surf = sp orelse return;
    defer rel(surf);

    // CreateBitmapFromDxgiSurface [vtable[62]]
    const bp = BitmapProperties1{
        .format = DXGI_FORMAT_B8G8R8A8_UNORM, .alphaMode = 1,
        .dpiX = 96, .dpiY = 96,
        .bitmapOptions = 3, // TARGET | CANNOT_DRAW
        ._pad = 0, .colorContext = null,
    };
    var bm: ?*anyopaque = null;
    if (@as(*const fn (*anyopaque, *anyopaque, *const BitmapProperties1, *?*anyopaque) callconv(.winapi) w.LONG,
            @ptrCast(vt(ctx)[62]))(ctx, surf, &bp, &bm) != 0) return;
    const bitmap = bm orelse return;
    defer rel(bitmap);

    // SetTarget → BeginDraw → draw → EndDraw → SetTarget(null)
    @as(*const fn (*anyopaque, ?*anyopaque) callconv(.winapi) void, @ptrCast(vt(ctx)[74]))(ctx, bitmap);
    @as(*const fn (*anyopaque) callconv(.winapi) void, @ptrCast(vt(ctx)[48]))(ctx);

    drawToolbar(ctx);

    _ = @as(*const fn (*anyopaque, ?*u64, ?*u64) callconv(.winapi) w.LONG, @ptrCast(vt(ctx)[49]))(ctx, null, null);
    @as(*const fn (*anyopaque, ?*anyopaque) callconv(.winapi) void, @ptrCast(vt(ctx)[74]))(ctx, null);

    // Present [vtable[8]]
    _ = @as(*const fn (*anyopaque, u32, u32) callconv(.winapi) w.LONG, @ptrCast(vt(sc)[8]))(sc, 0, 0);
}

fn drawToolbar(ctx: *anyopaque) void {
    const W: f32 = @floatFromInt(g_w);
    const H: f32 = @floatFromInt(g_h);
    const B: f32  = @floatFromInt(g_btn);
    const g: f32  = @floatFromInt(g_gap);
    const ml: f32 = @floatFromInt(g_ml);
    const sep: f32 = W - 8.0*B - ml - ml;
    const vy: f32 = (H - B) / 2.0;
    const radius: f32 = (B - 2.0*g) * 0.22;

    @as(*const fn (*anyopaque, *const ColorF) callconv(.winapi) void,
        @ptrCast(vt(ctx)[47]))(ctx, &ColorF{ .r=0, .g=0, .b=0, .a=0 });

    const btn_n  = createBrush(ctx, .{ .r=0.07, .g=0.07, .b=0.07, .a=0.07 }) orelse return;
    defer rel(btn_n);
    const btn_hv = createBrush(ctx, .{ .r=0.18, .g=0.18, .b=0.18, .a=0.18 }) orelse return;
    defer rel(btn_hv);
    const icon_br = createBrush(ctx, .{ .r=0.85, .g=0.85, .b=0.85, .a=0.85 }) orelse return;
    defer rel(icon_br);

    const is_muted = audio.getMute();
    const volume   = audio.getVolume();

    // Green brush for active play state
    const play_green: ?*anyopaque = if (g_playing)
        createBrush(ctx, .{ .r=0.0, .g=0.60, .b=0.22, .a=0.28 }) else null;
    defer if (play_green) |b| rel(b);

    // Three media buttons
    for ([3]HitZone{ .prev, .play, .next }, 0..) |zone, i| {
        const bx   = ml + @as(f32, @floatFromInt(i)) * B;
        const slot = RectF{ .left=bx+g, .top=vy+g, .right=bx+B-g, .bottom=vy+B-g };
        const bg: *anyopaque = if (zone == .play and play_green != null)
            play_green.?
        else if (g_hover == zone) btn_hv else btn_n;
        fillRoundedRect(ctx, .{ .rect=slot, .radiusX=radius, .radiusY=radius }, bg);
        const icon: []const u16 = switch (zone) {
            .prev  => &ICON_PREV,
            .play  => if (g_playing) &ICON_PAUSE else &ICON_PLAY,
            .next  => &ICON_NEXT,
            else   => &ICON_PLAY,
        };
        drawIcon(ctx, icon, slot, icon_br);
    }

    // Separator
    const sep_br = createBrush(ctx, .{ .r=0.039, .g=0.039, .b=0.039, .a=0.196 }) orelse return;
    defer rel(sep_br);
    const sx = ml + 3.0 * B;
    fillRect(ctx, .{ .left=sx, .top=vy+B*0.15, .right=sx+sep, .bottom=vy+B*0.85 }, sep_br);

    // Slider background (4B wide)
    const sld_n  = createBrush(ctx, .{ .r=0.019, .g=0.019, .b=0.019, .a=0.137 }) orelse return;
    defer rel(sld_n);
    const sld_hv = createBrush(ctx, .{ .r=0.033, .g=0.033, .b=0.033, .a=0.18  }) orelse return;
    defer rel(sld_hv);
    const sld_x0  = sx + sep;
    const sld_slot = RectF{ .left=sld_x0+g, .top=vy+g, .right=sld_x0+4.0*B-g, .bottom=vy+B-g };
    fillRoundedRect(ctx, .{ .rect=sld_slot, .radiusX=radius, .radiusY=radius },
        if (g_hover == .slider) sld_hv else sld_n);

    // Volume fill bar
    if (volume > 0.005) {
        const fill_w  = (sld_slot.right - sld_slot.left) * volume;
        const fill_br = createBrush(ctx, .{ .r=0.30, .g=0.30, .b=0.30, .a=0.30 }) orelse return;
        defer rel(fill_br);
        fillRoundedRect(ctx, .{ .rect=.{ .left=sld_slot.left, .top=sld_slot.top,
                         .right=sld_slot.left+fill_w, .bottom=sld_slot.bottom },
                         .radiusX=radius, .radiusY=radius }, fill_br);
    }

    // Slider: icon + "N%" text side by side, centered as a block
    const vol_icon: []const u16 = if (is_muted) &ICON_MUTE
        else if (volume < 0.33) &ICON_VOL0
        else if (volume < 0.67) &ICON_VOL1
        else &ICON_VOL2;

    const vol_pct: u32 = @intFromFloat(@max(0.0, @min(100.0, volume * 100.0 + 0.5)));
    var buf8: [8]u8   = undefined;
    const s8 = std.fmt.bufPrint(&buf8, "{d}%", .{vol_pct}) catch return;
    var buf16: [8]u16 = undefined;
    for (s8, 0..) |c, j| buf16[j] = c;

    const icon_w: f32 = B * 0.65;
    const text_w: f32 = B * 0.95;
    const blk_x       = (sld_slot.left + sld_slot.right) * 0.5 - (icon_w + text_w) * 0.5;
    drawIcon(ctx, vol_icon,
        .{ .left=blk_x, .top=sld_slot.top, .right=blk_x+icon_w, .bottom=sld_slot.bottom },
        icon_br);
    drawText(ctx, buf16[0..s8.len],
        .{ .left=blk_x+icon_w, .top=sld_slot.top, .right=blk_x+icon_w+text_w, .bottom=sld_slot.bottom },
        icon_br);

    // Volume button — red when muted
    const vol_red: ?*anyopaque = if (is_muted)
        createBrush(ctx, .{ .r=0.65, .g=0.0, .b=0.0, .a=0.28 }) else null;
    defer if (vol_red) |b| rel(b);

    const vol_x   = sld_x0 + 4.0 * B;
    const vol_slot = RectF{ .left=vol_x+g, .top=vy+g, .right=vol_x+B-g, .bottom=vy+B-g };
    const vol_bg: *anyopaque = if (vol_red != null) vol_red.?
        else if (g_hover == .vol) btn_hv else btn_n;
    fillRoundedRect(ctx, .{ .rect=vol_slot, .radiusX=radius, .radiusY=radius }, vol_bg);
    drawIcon(ctx, if (is_muted) &ICON_MUTE else &ICON_VOL, vol_slot, icon_br);
}
