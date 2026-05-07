// Phase 5: D3D11 + DXGI swap chain + D2D1 DeviceContext, wired into Composition.
// All DLLs loaded dynamically (d3d11/d2d1 not in Zig bundled SDK).

const w     = @import("win32.zig");
const comp  = @import("composition.zig");
const audio = @import("audio.zig");
pub const HitZone = enum { none, prev, play, next, slider, vol };

// ── Constants ─────────────────────────────────────────────────────────────────

const DXGI_FORMAT_B8G8R8A8_UNORM     : i32 = 87;
const DXGI_USAGE_RENDER_TARGET_OUTPUT : u32 = 0x20;
const DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL: i32 = 3;
const DXGI_ALPHA_MODE_PREMULTIPLIED  : i32 = 1;
const DXGI_SCALING_STRETCH           : i32 = 0;
const D3D11_SDK_VERSION              : u32 = 7;
const D3D_DRIVER_TYPE_HARDWARE       : i32 = 1;
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
    format:        i32,
    alphaMode:     i32,
    dpiX:          f32,
    dpiY:          f32,
    bitmapOptions: i32,
    colorContext:  ?*anyopaque,
};

pub const RectF       = extern struct { left: f32, top: f32, right: f32, bottom: f32 };
pub const RoundedRect = extern struct { rect: RectF, radiusX: f32, radiusY: f32 };

// ── Dynamic loading ───────────────────────────────────────────────────────────

const FnD3D11 = *const fn (?*anyopaque, i32, ?*anyopaque, u32, ?*anyopaque, u32, u32,
    *?*anyopaque, ?*anyopaque, ?*anyopaque) callconv(.winapi) w.LONG;
const FnD2D1  = *const fn (i32, *const GUID, ?*anyopaque, *?*anyopaque) callconv(.winapi) w.LONG;



const vt  = comp.vtbl;
const qi  = comp.qi;
const rel = comp.release;

// ── State ─────────────────────────────────────────────────────────────────────

var g_sc      : ?*anyopaque = null;
var g_ctx     : ?*anyopaque = null;
var g_h       : u32 = 0;
var g_btn     : u32 = 0;
var g_ml      : u32 = 0;
var g_hover   : HitZone = .none;
var g_dw_fmt  : ?*anyopaque = null;
var g_playing : bool = false;
var g_br_n    : ?*anyopaque = null;
var g_br_hv   : ?*anyopaque = null;
var g_br_icon : ?*anyopaque = null;

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

pub fn init(compositor: *anyopaque, root_vis: *anyopaque, width: u32, height: u32, btn_size: u32, margin_l: u32) void {
    const l3 = w.loadLibrary("d3d11.dll") orelse w.exit(1);
    const l2 = w.loadLibrary("d2d1.dll")  orelse w.exit(1);
    const fn_d3d11: FnD3D11 = @ptrCast(w.getProcAddress(l3, "D3D11CreateDevice") orelse w.exit(1));
    const fn_d2d1:  FnD2D1  = @ptrCast(w.getProcAddress(l2, "D2D1CreateFactory") orelse w.exit(1));

    var d3d: ?*anyopaque = null;
    if (fn_d3d11(null, D3D_DRIVER_TYPE_HARDWARE, null, D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            null, 0, D3D11_SDK_VERSION, &d3d, null, null) != 0) w.exit(1);
    const d3d_dev = d3d orelse w.exit(1);
    defer rel(d3d_dev);

    const dxgi_dev = qi(d3d_dev, &IID_IDXGIDevice) orelse w.exit(1);
    defer rel(dxgi_dev);

    var fptr: ?*anyopaque = null;
    if (fn_d2d1(0, &IID_ID2D1Factory1, null, &fptr) != 0) w.exit(1);
    const fact = fptr orelse w.exit(1);
    defer rel(fact);

    var d2d_dev: ?*anyopaque = null;
    if (@as(*const fn (*anyopaque, *anyopaque, *?*anyopaque) callconv(.winapi) w.LONG,
            @ptrCast(vt(fact)[17]))(fact, dxgi_dev, &d2d_dev) != 0) w.exit(1);
    const ddev = d2d_dev orelse w.exit(1);
    defer rel(ddev);

    var ctx: ?*anyopaque = null;
    if (@as(*const fn (*anyopaque, i32, *?*anyopaque) callconv(.winapi) w.LONG,
            @ptrCast(vt(ddev)[4]))(ddev, 0, &ctx) != 0) w.exit(1);
    const d2d_ctx = ctx orelse w.exit(1);

    var adp: ?*anyopaque = null;
    if (@as(*const fn (*anyopaque, *?*anyopaque) callconv(.winapi) w.LONG,
            @ptrCast(vt(dxgi_dev)[7]))(dxgi_dev, &adp) != 0) w.exit(1);
    const adapter = adp orelse w.exit(1);
    defer rel(adapter);

    var f2: ?*anyopaque = null;
    if (@as(*const fn (*anyopaque, *const GUID, *?*anyopaque) callconv(.winapi) w.LONG,
            @ptrCast(vt(adapter)[6]))(adapter, &IID_IDXGIFactory2, &f2) != 0) w.exit(1);
    const factory2 = f2 orelse w.exit(1);
    defer rel(factory2);

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
            @ptrCast(vt(factory2)[24]))(factory2, dxgi_dev, &desc, null, &sc) != 0) w.exit(1);
    const swap_chain = sc orelse w.exit(1);

    const ci = qi(compositor, &comp.IID_ICompositorInterop) orelse w.exit(1);
    defer rel(ci);
    var csurf_raw: ?*anyopaque = null;
    if (@as(*const fn (*anyopaque, *anyopaque, *?*anyopaque) callconv(.winapi) w.LONG,
            @ptrCast(vt(ci)[4]))(ci, swap_chain, &csurf_raw) != 0) w.exit(1);
    const csurf = csurf_raw orelse w.exit(1);
    defer rel(csurf);

    var brush_raw: ?*anyopaque = null;
    if (@as(*const fn (*anyopaque, *anyopaque, *?*anyopaque) callconv(.winapi) w.LONG,
            @ptrCast(vt(compositor)[24]))(compositor, csurf, &brush_raw) != 0) w.exit(1);
    const brush = brush_raw orelse w.exit(1);
    defer rel(brush);

    if (qi(brush, &comp.IID_ICompositionSurfaceBrush)) |sb| {
        defer rel(sb);
        _ = @as(*const fn (*anyopaque, w.INT) callconv(.winapi) w.LONG, @ptrCast(vt(sb)[11]))(sb, 2);
    }
    if (qi(root_vis, &comp.IID_ISpriteVisual)) |sv| {
        defer rel(sv);
        if (qi(brush, &comp.IID_IInspectable)) |bi| {
            defer rel(bi);
            _ = @as(*const fn (*anyopaque, *anyopaque) callconv(.winapi) w.LONG, @ptrCast(vt(sv)[7]))(sv, bi);
        }
    }

    g_sc     = swap_chain;
    g_ctx    = d2d_ctx;
    g_h      = height;
    g_btn    = btn_size;
    g_ml     = margin_l;
    g_br_n    = createBrush(d2d_ctx, .{ .r=0.07, .g=0.07, .b=0.07, .a=0.07 });
    g_br_hv   = createBrush(d2d_ctx, .{ .r=0.36, .g=0.36, .b=0.36, .a=0.36 });
    g_br_icon = createBrush(d2d_ctx, .{ .r=0.85, .g=0.85, .b=0.85, .a=0.85 });
    dwrite: {
        const dw_lib  = w.loadLibrary("dwrite.dll") orelse break :dwrite;
        const dw_proc = w.getProcAddress(dw_lib, "DWriteCreateFactory") orelse break :dwrite;
        const FnCreate = *const fn (i32, *const GUID, *?*anyopaque) callconv(.winapi) w.LONG;
        var dw_raw: ?*anyopaque = null;
        if (@as(FnCreate, @ptrCast(dw_proc))(0, &IID_IDWriteFactory, &dw_raw) != 0) break :dwrite;
        const factory = dw_raw orelse break :dwrite;
        const FnFmt   = *const fn (*anyopaque, [*:0]const u16, ?*anyopaque, i32, i32, i32, f32, [*:0]const u16, *?*anyopaque) callconv(.winapi) w.LONG;
        const FnAlign = *const fn (*anyopaque, i32) callconv(.winapi) w.LONG;
        var dw_fmt: ?*anyopaque = null;
        if (@as(FnFmt, @ptrCast(vt(factory)[15]))(factory,
                w.L("Segoe MDL2 Assets"), null, 400, 0, 5,
                @as(f32, @floatFromInt(btn_size)) * 0.52, w.L(""), &dw_fmt) == 0) {
            const tf = dw_fmt orelse break :dwrite;
            _ = @as(FnAlign, @ptrCast(vt(tf)[3]))(tf, 2);
            _ = @as(FnAlign, @ptrCast(vt(tf)[4]))(tf, 2);
            g_dw_fmt = tf;
        }
    }
}

// ── D2D1 draw helpers ─────────────────────────────────────────────────────────

fn createBrush(ctx: *anyopaque, color: ColorF) ?*anyopaque {
    var out: ?*anyopaque = null;
    _ = @as(*const fn (*anyopaque, *const ColorF, ?*anyopaque, *?*anyopaque) callconv(.winapi) w.LONG,
        @ptrCast(vt(ctx)[8]))(ctx, &color, null, &out);
    return out;
}

pub fn fillRoundedRect(ctx: *anyopaque, rr: RoundedRect, brush: *anyopaque) void {
    @as(*const fn (*anyopaque, *const RoundedRect, *anyopaque) callconv(.winapi) void,
        @ptrCast(vt(ctx)[19]))(ctx, &rr, brush);
}

fn drawGlyph(ctx: *anyopaque, text: []const u16, rect: RectF, brush: *anyopaque) void {
    const f = g_dw_fmt orelse return;
    const Fn = *const fn (*anyopaque, [*]const u16, u32, *anyopaque, *const RectF, *anyopaque, u32, i32) callconv(.winapi) void;
    @as(Fn, @ptrCast(vt(ctx)[27]))(ctx, text.ptr, @intCast(text.len), f, &rect, brush, 0, 0);
}

// ── Render ─────────────────────────────────────────────────────────────────────

pub fn render() void {
    const sc  = g_sc  orelse return;
    const ctx = g_ctx orelse return;

    var sp: ?*anyopaque = null;
    if (@as(*const fn (*anyopaque, u32, *const GUID, *?*anyopaque) callconv(.winapi) w.LONG,
            @ptrCast(vt(sc)[9]))(sc, 0, &IID_IDXGISurface, &sp) != 0) return;
    const surf = sp orelse return;
    defer rel(surf);

    const bp = BitmapProperties1{
        .format = DXGI_FORMAT_B8G8R8A8_UNORM, .alphaMode = 1,
        .dpiX = 96, .dpiY = 96,
        .bitmapOptions = 3,
        .colorContext = null,
    };
    var bm: ?*anyopaque = null;
    if (@as(*const fn (*anyopaque, *anyopaque, *const BitmapProperties1, *?*anyopaque) callconv(.winapi) w.LONG,
            @ptrCast(vt(ctx)[62]))(ctx, surf, &bp, &bm) != 0) return;
    const bitmap = bm orelse return;
    defer rel(bitmap);

    @as(*const fn (*anyopaque, ?*anyopaque) callconv(.winapi) void, @ptrCast(vt(ctx)[74]))(ctx, bitmap);
    @as(*const fn (*anyopaque) callconv(.winapi) void, @ptrCast(vt(ctx)[48]))(ctx);

    drawToolbar(ctx);

    _ = @as(*const fn (*anyopaque, ?*u64, ?*u64) callconv(.winapi) w.LONG, @ptrCast(vt(ctx)[49]))(ctx, null, null);
    @as(*const fn (*anyopaque, ?*anyopaque) callconv(.winapi) void, @ptrCast(vt(ctx)[74]))(ctx, null);

    _ = @as(*const fn (*anyopaque, u32, u32) callconv(.winapi) w.LONG, @ptrCast(vt(sc)[8]))(sc, 0, 0);
}

fn drawToolbar(ctx: *anyopaque) void {
    const H: f32  = @floatFromInt(g_h);
    const B: f32  = @floatFromInt(g_btn);
    const ml: f32 = @floatFromInt(g_ml);
    const vy: f32 = (H - B) / 2.0;
    const g        = vy;
    const radius: f32 = (B - 2.0*g) * 0.22;

    @as(*const fn (*anyopaque, *const ColorF) callconv(.winapi) void,
        @ptrCast(vt(ctx)[47]))(ctx, &ColorF{ .r=0, .g=0, .b=0, .a=0 });

    const btn_n   = g_br_n    orelse return;
    const btn_hv  = g_br_hv   orelse return;
    const icon_br = g_br_icon orelse return;

    const is_muted = audio.getMute();
    const volume   = audio.getVolume();

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
            else   => unreachable,
        };
        drawGlyph(ctx, icon, slot, icon_br);
    }

    // Slider background (4B wide, starts right after the three buttons)
    const sld_x0   = ml + 3.0 * B;
    const sld_slot = RectF{ .left=sld_x0+g, .top=vy+g, .right=sld_x0+4.0*B-g, .bottom=vy+B-g };
    fillRoundedRect(ctx, .{ .rect=sld_slot, .radiusX=radius, .radiusY=radius },
        if (g_hover == .slider) btn_hv else btn_n);

    // Volume fill bar
    if (volume > 0.005) {
        const fill_w  = (sld_slot.right - sld_slot.left) * volume;
        const fill_br = createBrush(ctx, .{ .r=0.60, .g=0.60, .b=0.60, .a=0.60 }) orelse return;
        defer rel(fill_br);
        fillRoundedRect(ctx, .{ .rect=.{ .left=sld_slot.left, .top=sld_slot.top,
                         .right=sld_slot.left+fill_w, .bottom=sld_slot.bottom },
                         .radiusX=radius, .radiusY=radius }, fill_br);
    }

    // Volume button — red when muted
    const vol_red: ?*anyopaque = if (is_muted)
        createBrush(ctx, .{ .r=0.65, .g=0.0, .b=0.0, .a=0.28 }) else null;
    defer if (vol_red) |b| rel(b);

    const vol_x    = sld_x0 + 4.0 * B;
    const vol_slot = RectF{ .left=vol_x+g, .top=vy+g, .right=vol_x+B-g, .bottom=vy+B-g };
    const vol_bg: *anyopaque = if (vol_red != null) vol_red.?
        else if (g_hover == .vol) btn_hv else btn_n;
    fillRoundedRect(ctx, .{ .rect=vol_slot, .radiusX=radius, .radiusY=radius }, vol_bg);
    drawGlyph(ctx, if (is_muted) &ICON_MUTE else &ICON_VOL, vol_slot, icon_br);
}
