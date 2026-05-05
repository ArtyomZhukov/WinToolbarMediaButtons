using System.Numerics;
using System.Runtime.InteropServices;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using WinRT;
using WinToolbarMediaButtons.Services;

namespace WinToolbarMediaButtons;

sealed class ToolbarWindow : IDisposable
{
    // ── P/Invoke ──────────────────────────────────────────────────────────────

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr RegisterClassEx(ref WNDCLASSEX wc);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateWindowEx(uint exStyle, string cls, string? title,
        uint style, int x, int y, int w, int h,
        IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);

    [DllImport("user32.dll")] static extern bool    DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr  DefWindowProc(IntPtr hWnd, uint msg, nuint wp, nint lp);
    [DllImport("user32.dll")] static extern IntPtr  FindWindow(string cls, string? title);
    [DllImport("user32.dll")] static extern bool    GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] static extern bool    SetWindowPos(IntPtr hWnd, IntPtr hWndAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern IntPtr  SetParent(IntPtr hWnd, IntPtr hWndNew);
    [DllImport("user32.dll")] static extern int     SetWindowLong(IntPtr hWnd, int nIndex, uint newLong);
    [DllImport("user32.dll")] static extern uint    GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] static extern IntPtr  LoadCursor(IntPtr hInstance, int cursor);
    [DllImport("user32.dll")] static extern bool    TrackMouseEvent(ref TRACKMOUSEEVENT tme);
    [DllImport("user32.dll")] static extern bool    PostMessage(IntPtr hWnd, uint msg, nuint wp, nint lp);
    [DllImport("user32.dll")] static extern IntPtr  SetCapture(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool    ReleaseCapture();
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string? n);

    [DllImport("CoreMessaging.dll")]
    static extern int CreateDispatcherQueueController(
        DispatcherQueueOptions o,
        [MarshalAs(UnmanagedType.IUnknown)] out object result);

    // ── COM ───────────────────────────────────────────────────────────────────

    [ComImport, Guid("29E691FA-4567-4DCA-B319-D0F207EB6807")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICompositorDesktopInterop
    {
        [PreserveSig] int CreateDesktopWindowTarget(IntPtr hwnd, bool topmost, out IntPtr result);
        [PreserveSig] int EnsureOnThread(uint tid);
    }

    // GUID: 25297D5C-3AD4-4C9C-B5CF-E36A38512330
    [ComImport, Guid("25297D5C-3AD4-4C9C-B5CF-E36A38512330")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICompositorInterop
    {
        [PreserveSig] int CreateCompositionSurfaceForHandle(IntPtr handle, out IntPtr result);
        [PreserveSig] int CreateCompositionSurfaceForSwapChain(IntPtr swapChain, out IntPtr result);
        [PreserveSig] int CreateGraphicsDevice(IntPtr renderingDevice, out IntPtr result);
    }

    // GUID: CEA8D6A1-32C3-45CF-9AC3-BB0B5F53BDE1
    [ComImport, Guid("CEA8D6A1-32C3-45CF-9AC3-BB0B5F53BDE1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICompositionDrawingSurfaceInterop
    {
        [PreserveSig] int BeginDraw(IntPtr updateRect, ref Guid iid, out IntPtr updateObject, out POINT updateOffset);
        [PreserveSig] int EndDraw();
        [PreserveSig] int Resize(SIZE sizePixels);
        [PreserveSig] int Scroll(IntPtr scrollRect, IntPtr clipRect, int offsetX, int offsetY);
        [PreserveSig] int SuspendDraw();
        [PreserveSig] int ResumeDraw();
    }

    [DllImport("d3d11.dll")]
    static extern int D3D11CreateDevice(
        IntPtr pAdapter, int DriverType, IntPtr Software, uint Flags,
        IntPtr pFeatureLevels, uint FeatureLevels, uint SDKVersion,
        out IntPtr ppDevice, IntPtr pFeatureLevel, IntPtr ppImmediateContext);

    [DllImport("d2d1.dll")]
    static extern int D2D1CreateFactory(
        int factoryType, ref Guid riid, IntPtr pOptions, out IntPtr ppFactory);

    // ── Structs / delegates ───────────────────────────────────────────────────

    delegate nint WndProcDelegate(IntPtr hWnd, uint msg, nuint wp, nint lp);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WNDCLASSEX
    {
        public uint    cbSize, style;
        public IntPtr  lpfnWndProc;
        public int     cbClsExtra, cbWndExtra;
        public IntPtr  hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName;
        public string  lpszClassName;
        public IntPtr  hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct DispatcherQueueOptions { public int dwSize, threadType, apartmentType; }

    [StructLayout(LayoutKind.Sequential)]
    struct TRACKMOUSEEVENT { public uint cbSize, dwFlags; public IntPtr hwndTrack; public uint dwHoverTime; }

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct SIZE  { public int cx, cy; }

    // D2D1 structs for bitmap upload
    [StructLayout(LayoutKind.Sequential)] struct D2D1_SIZE_U  { public uint width, height; }
    [StructLayout(LayoutKind.Sequential)] struct D2D1_PIXEL_FORMAT { public int format, alphaMode; }
    [StructLayout(LayoutKind.Sequential)] struct D2D1_BITMAP_PROPERTIES { public D2D1_PIXEL_FORMAT pixelFormat; public float dpiX, dpiY; }
    [StructLayout(LayoutKind.Sequential)] struct D2D1_COLOR_F { public float r, g, b, a; }
    [StructLayout(LayoutKind.Sequential)] struct D2D1_MATRIX_3X2_F { public float _11, _12, _21, _22, _31, _32; }

    [StructLayout(LayoutKind.Sequential)]
    struct DXGI_SWAP_CHAIN_DESC1
    {
        public uint Width, Height;
        public int  Format;         // DXGI_FORMAT
        public int  Stereo;         // BOOL
        public uint SampleCount, SampleQuality;
        public uint BufferUsage;
        public uint BufferCount;
        public int  Scaling;        // DXGI_SCALING
        public int  SwapEffect;     // DXGI_SWAP_EFFECT
        public int  AlphaMode;      // DXGI_ALPHA_MODE
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct D2D1_BITMAP_PROPERTIES1
    {
        public D2D1_PIXEL_FORMAT pixelFormat;
        public float  dpiX, dpiY;
        public int    bitmapOptions; // D2D1_BITMAP_OPTIONS
        private int   _pad;          // 64-bit alignment before pointer
        public IntPtr colorContext;
    }

    // ── Constants ─────────────────────────────────────────────────────────────

    const uint WS_CHILD        = 0x40000000;
    const uint WS_POPUP        = 0x80000000;
    const uint WS_VISIBLE      = 0x10000000;
    const uint WS_CLIPSIBLINGS = 0x04000000;
    const uint WS_CLIPCHILDREN = 0x02000000;
    const uint WS_EX_TOOLWINDOW = 0x00000080;
    const uint WS_EX_NOACTIVATE = 0x08000000;
    const int  GWL_STYLE        = -16;
    const uint SWP_NOACTIVATE   = 0x0010;
    const uint SWP_FRAMECHANGED = 0x0020;
    const uint TME_LEAVE        = 0x00000002;
    const int  IDC_ARROW        = 32512;

    const uint WM_LBUTTONDOWN = 0x0201;
    const uint WM_LBUTTONUP   = 0x0202;
    const uint WM_MOUSEMOVE   = 0x0200;
    const uint WM_MOUSELEAVE  = 0x02A3;
    const uint WM_PLAY_STATE  = 0x8001;
    const uint WM_VOL_SYNC    = 0x8002;

    const int DQTYPE_THREAD_CURRENT = 2;
    const int DQTAT_COM_STA         = 1;

    // ── Layout (logical pixels, same as taskbar height = 52) ─────────────────
    //
    //  [4]Prev  [58]Play  [112]Next  167|  [189]Mute  [243..399]Slider
    //
    const int LogicalWidth  = 399;
    const int LogicalHeight = 52;

    static readonly int[] BtnX = [4, 58, 112, 189]; // Prev, Play, Next, Mute
    const int SliderX = 243, SliderW = 156;
    const int Pad = 6, BtnVis = 40;   // visual inset: 52 - 6 - 6 = 40
    const float Corner = 6f;

    const int BTN_PREV = 0, BTN_PLAY = 1, BTN_NEXT = 2, BTN_MUTE = 3;

    const int SliderIconSz = 24;   // speaker icon inside slider, logical px
    const int SliderTextW  = 38;   // % text area inside slider, logical px

    // Segoe Fluent Icons glyphs
    const char GlyphPrev  = '';
    const char GlyphPlay  = '';
    const char GlyphPause = '';
    const char GlyphNext  = '';
    const char GlyphVol0  = ''; // speaker, no bars
    const char GlyphVol1  = ''; // speaker, low
    const char GlyphSnd   = ''; // speaker, medium
    const char GlyphVol3  = ''; // speaker, high
    const char GlyphMute  = ''; // muted

    const string WndClass = "WinToolbarMB";
    const string LogPath  = @"C:\Temp\toolbar_diag.txt";

    // ── Fields ────────────────────────────────────────────────────────────────

    static WndProcDelegate? s_proc;
    static ToolbarWindow?   s_inst;

    readonly object              _dqc;
    readonly Compositor          _compositor;
    readonly DesktopWindowTarget _target;
    readonly ContainerVisual     _root;
    readonly IntPtr              _hwnd;

    float _scale;
    int   _physH;

    VolumeEndpointService?  _volSvc;
    WasapiMonitorService?   _wasapi;
    System.Threading.Timer? _volTimer;

    // Button background brushes (hover / press states)
    readonly CompositionColorBrush[] _btnBgBrush = new CompositionColorBrush[4];

    // Button glyph visuals (surface brush swapped on state change)
    readonly SpriteVisual[] _btnGlyph = new SpriteVisual[4];
    CompositionSurfaceBrush?[] _playBrush = new CompositionSurfaceBrush?[2]; // [0]=▶ [1]=⏸
    CompositionSurfaceBrush?[] _muteBrush = new CompositionSurfaceBrush?[2]; // [0]=🔊 [1]=🔇

    SpriteVisual?          _volFill;
    CompositionColorBrush? _volFillBrush;
    SpriteVisual?          _volSliderIcon;   // speaker/mute icon inside slider
    SpriteVisual?          _volTextVis;      // % text inside slider

    IntPtr _d2dCtxPtr;                       // kept alive for dynamic text updates
    IntPtr _volTextScPtr;                    // swap chain for % text
    int    _volTextPxW, _volTextPxH;
    float  _lastVol;
    CompositionSurfaceBrush?[] _sldIconBrush = new CompositionSurfaceBrush?[4];

    bool  _mouseTracking;
    int   _hovered = -1;   // -1 none | 0-3 button | 4 slider
    int   _pressed = -1;
    bool  _dragging;
    bool  _muted;
    bool  _isPlaying;
    bool  _disposed;

    // ── Logging ───────────────────────────────────────────────────────────────

    static void Log(string msg)
    {
        try
        {
            Directory.CreateDirectory(@"C:\Temp");
            File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] TW: {msg}\r\n");
        }
        catch { }
    }

    // ── Constructor ───────────────────────────────────────────────────────────

    public ToolbarWindow()
    {
        Log("ctor start");

        CreateDispatcherQueueController(new DispatcherQueueOptions
        {
            dwSize        = Marshal.SizeOf<DispatcherQueueOptions>(),
            threadType    = DQTYPE_THREAD_CURRENT,
            apartmentType = DQTAT_COM_STA,
        }, out _dqc);

        s_proc = WndProc;
        var wc = new WNDCLASSEX
        {
            cbSize        = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc   = Marshal.GetFunctionPointerForDelegate(s_proc),
            hInstance     = GetModuleHandle(null),
            hCursor       = LoadCursor(IntPtr.Zero, IDC_ARROW),
            lpszClassName = WndClass,
        };
        RegisterClassEx(ref wc);

        var taskbar = FindWindow("Shell_TrayWnd", null);
        GetWindowRect(taskbar, out var tb);
        _physH = tb.Bottom - tb.Top;
        _scale = _physH > 0 ? _physH / (float)LogicalHeight : 1f;
        int w  = (int)Math.Ceiling(LogicalWidth * _scale) + 1;
        Log($"taskbar h={_physH} scale={_scale:F3} w={w}");

        // Step 1: top-level popup — required before DCOMP setup
        _hwnd = CreateWindowEx(
            WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, WndClass, null,
            WS_POPUP | WS_VISIBLE | WS_CLIPCHILDREN | WS_CLIPSIBLINGS,
            tb.Left, tb.Top, w, _physH,
            IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastWin32Error()}");

        // Step 2: build DCOMP tree while still top-level
        _compositor = new Compositor();
        var interop = _compositor.As<ICompositorDesktopInterop>();
        int hr = interop.CreateDesktopWindowTarget(_hwnd, true, out var ptr);
        if (hr != 0 || ptr == IntPtr.Zero)
            throw new InvalidOperationException($"CreateDesktopWindowTarget hr=0x{hr:X8}");

        _target = DesktopWindowTarget.FromAbi(ptr);
        Marshal.Release(ptr);

        _root = _compositor.CreateContainerVisual();
        _root.RelativeSizeAdjustment = Vector2.One;
        _target.Root = _root;

        BuildUI();

        // Step 3: reparent → Shell Z-band
        SetParent(_hwnd, taskbar);
        uint style = GetWindowLong(_hwnd, GWL_STYLE);
        SetWindowLong(_hwnd, GWL_STYLE, (style & ~WS_POPUP) | WS_CHILD);
        SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, w, _physH, SWP_NOACTIVATE | SWP_FRAMECHANGED);

        // Step 4: services
        s_inst = this;

        try
        {
            _volSvc = new VolumeEndpointService();
            _muted  = _volSvc.GetMute();
            RefreshBtnVisual(BTN_MUTE);
            UpdateVolFill(_volSvc.GetVolume());
        }
        catch (Exception ex) { Log($"VolumeService: {ex.Message}"); }

        try
        {
            _wasapi = new WasapiMonitorService();
            _wasapi.StateChanged += () => PostMessage(_hwnd, WM_PLAY_STATE, 0, 0);
        }
        catch (Exception ex) { Log($"WasapiMonitor: {ex.Message}"); }

        _volTimer = new System.Threading.Timer(
            _ => PostMessage(_hwnd, WM_VOL_SYNC, 0, 0), null, 300, 300);

        // Step 5: load glyphs async (LoadedImageSurface fires on UI thread via DispatcherQueue)
        LoadGlyphs();

        Log("ctor done");
    }

    // ── UI ────────────────────────────────────────────────────────────────────

    void BuildUI()
    {
        float P(float l) => l * _scale;

        // Background
        var bg = _compositor.CreateSpriteVisual();
        bg.RelativeSizeAdjustment = Vector2.One;
        bg.Brush = _compositor.CreateColorBrush(C(255, 0x1C, 0x1C, 0x1C));
        _root.Children.InsertAtBottom(bg);

        // Button background shape visuals (rounded rect, for hover/press)
        for (int i = 0; i < 4; i++)
        {
            var geo = _compositor.CreateRoundedRectangleGeometry();
            geo.Size         = new Vector2(P(BtnVis), P(BtnVis));
            geo.CornerRadius = new Vector2(P(Corner), P(Corner));

            var brush = _compositor.CreateColorBrush(C(0, 255, 255, 255));
            var shape = _compositor.CreateSpriteShape(geo);
            shape.FillBrush = brush;

            var sv = _compositor.CreateShapeVisual();
            sv.Size   = new Vector2(P(BtnVis), P(BtnVis));
            sv.Offset = new Vector3(P(BtnX[i] + Pad), P(Pad), 0);
            sv.Shapes.Add(shape);
            _root.Children.InsertAtTop(sv);
            _btnBgBrush[i] = brush;
        }

        // Glyph sprite visuals (surface brush set later by LoadGlyphs)
        for (int i = 0; i < 4; i++)
        {
            var gv = _compositor.CreateSpriteVisual();
            gv.Size   = new Vector2(P(BtnVis), P(BtnVis));
            gv.Offset = new Vector3(P(BtnX[i] + Pad), P(Pad), 0);
            _root.Children.InsertAtTop(gv);
            _btnGlyph[i] = gv;
        }

        // Separator
        var sep = _compositor.CreateSpriteVisual();
        sep.Size   = new Vector2(P(1), P(32));
        sep.Offset = new Vector3(P(167), P(10), 0);
        sep.Brush  = _compositor.CreateColorBrush(C(50, 255, 255, 255));
        _root.Children.InsertAtTop(sep);

        // Volume slider background
        var vgeo = _compositor.CreateRoundedRectangleGeometry();
        vgeo.Size         = new Vector2(P(SliderW), P(BtnVis));
        vgeo.CornerRadius = new Vector2(P(Corner), P(Corner));
        var vbgShape = _compositor.CreateSpriteShape(vgeo);
        vbgShape.FillBrush = _compositor.CreateColorBrush(C(35, 255, 255, 255));
        var vbgSv = _compositor.CreateShapeVisual();
        vbgSv.Size   = new Vector2(P(SliderW), P(BtnVis));
        vbgSv.Offset = new Vector3(P(SliderX), P(Pad), 0);
        vbgSv.Shapes.Add(vbgShape);
        _root.Children.InsertAtTop(vbgSv);

        // Volume fill bar (ends before text area)
        _volFillBrush = _compositor.CreateColorBrush(C(100, 255, 255, 255));
        _volFill = _compositor.CreateSpriteVisual();
        _volFill.Brush  = _volFillBrush;
        _volFill.Offset = new Vector3(P(SliderX + 2), P(Pad + 2), 0);
        _volFill.Size   = new Vector2(0, P(BtnVis - 4));
        _root.Children.InsertAtTop(_volFill);

        // Slider icon + % text: centered block overlaid on fill bar
        int sldBlockX = SliderX + SliderW / 2 - (SliderIconSz + 2 + SliderTextW) / 2;
        float sldIconY = Pad + (BtnVis - SliderIconSz) / 2f;
        var sIcon = _compositor.CreateSpriteVisual();
        sIcon.Size   = new Vector2(P(SliderIconSz), P(SliderIconSz));
        sIcon.Offset = new Vector3(P(sldBlockX), P(sldIconY), 0);
        _root.Children.InsertAtTop(sIcon);
        _volSliderIcon = sIcon;

        var sTxt = _compositor.CreateSpriteVisual();
        sTxt.Size   = new Vector2(P(SliderTextW), P(SliderIconSz));
        sTxt.Offset = new Vector3(P(sldBlockX + SliderIconSz + 2), P(sldIconY), 0);
        _root.Children.InsertAtTop(sTxt);
        _volTextVis = sTxt;
    }

    static Windows.UI.Color C(byte a, byte r, byte g, byte b)
        => Windows.UI.Color.FromArgb(a, r, g, b);

    // ── Glyph loading ──────────────────────────────────────────────────────────

    readonly List<IntPtr> _swapChains = new();

    void LoadGlyphs()
    {
        try
        {
            // 1. D3D11 device with BGRA support
            const uint BGRA = 0x20;
            int hr = D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, BGRA,
                IntPtr.Zero, 0, 7, out IntPtr d3dPtr, IntPtr.Zero, IntPtr.Zero);
            if (hr != 0 || d3dPtr == IntPtr.Zero)
                hr = D3D11CreateDevice(IntPtr.Zero, 5, IntPtr.Zero, BGRA,
                    IntPtr.Zero, 0, 7, out d3dPtr, IntPtr.Zero, IntPtr.Zero);
            if (d3dPtr == IntPtr.Zero) throw new Exception($"D3D11 hr=0x{hr:X8}");

            // 2. IDXGIDevice QI (shared by D2D chain and swap-chain factory chain)
            var iidDxgi = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
            hr = Marshal.QueryInterface(d3dPtr, ref iidDxgi, out IntPtr dxgiPtr);
            Marshal.Release(d3dPtr);
            if (hr != 0 || dxgiPtr == IntPtr.Zero) throw new Exception($"IDXGIDevice hr=0x{hr:X8}");

            // 3. ID2D1Factory1
            var iidFact1 = new Guid("bb12d362-daee-4b9a-aa1d-14ba401cfa1f");
            hr = D2D1CreateFactory(0, ref iidFact1, IntPtr.Zero, out IntPtr factPtr);
            if (hr != 0 || factPtr == IntPtr.Zero) { Marshal.Release(dxgiPtr); throw new Exception($"D2D1Factory1 hr=0x{hr:X8}"); }

            // 4. ID2D1Device via ID2D1Factory1::CreateDevice  (vtable[17])
            IntPtr d2dDevPtr;
            unsafe
            {
                var vt = *(IntPtr**)factPtr;
                hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)vt[17])(factPtr, dxgiPtr, &d2dDevPtr);
            }
            Marshal.Release(factPtr);
            if (hr != 0 || d2dDevPtr == IntPtr.Zero) { Marshal.Release(dxgiPtr); throw new Exception($"ID2D1Device hr=0x{hr:X8}"); }

            // 5. ID2D1DeviceContext via ID2D1Device::CreateDeviceContext  (vtable[4])
            IntPtr ctxPtr;
            unsafe
            {
                var vt = *(IntPtr**)d2dDevPtr;
                hr = ((delegate* unmanaged[Stdcall]<IntPtr, int, IntPtr*, int>)vt[4])(d2dDevPtr, 0, &ctxPtr);
            }
            Marshal.Release(d2dDevPtr);
            if (hr != 0 || ctxPtr == IntPtr.Zero) { Marshal.Release(dxgiPtr); throw new Exception($"ID2D1DevCtx hr=0x{hr:X8}"); }

            // 6. IDXGIAdapter via IDXGIDevice::GetAdapter  (vtable[7])
            IntPtr adapterPtr;
            unsafe
            {
                var vt = *(IntPtr**)dxgiPtr;
                hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)vt[7])(dxgiPtr, &adapterPtr);
            }
            if (hr != 0 || adapterPtr == IntPtr.Zero)
            { Marshal.Release(ctxPtr); Marshal.Release(dxgiPtr); throw new Exception($"GetAdapter hr=0x{hr:X8}"); }

            // 7. IDXGIFactory2 via IDXGIObject::GetParent  (vtable[6])
            var iidFact2 = new Guid("50c83a1c-e072-4c48-87b0-3630fa36a6d0");
            IntPtr dxgiFact2Ptr;
            unsafe
            {
                var vt = *(IntPtr**)adapterPtr;
                hr = ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)vt[6])(adapterPtr, &iidFact2, &dxgiFact2Ptr);
            }
            Marshal.Release(adapterPtr);
            if (hr != 0 || dxgiFact2Ptr == IntPtr.Zero)
            { Marshal.Release(ctxPtr); Marshal.Release(dxgiPtr); throw new Exception($"IDXGIFactory2 hr=0x{hr:X8}"); }

            int px = (int)Math.Ceiling(BtnVis * _scale);
            var compInterop = _compositor.As<ICompositorInterop>();

            _btnGlyph[BTN_PREV].Brush = MakeGlyphBrush(GlyphPrev,  px, dxgiPtr, dxgiFact2Ptr, ctxPtr, compInterop);
            _btnGlyph[BTN_NEXT].Brush = MakeGlyphBrush(GlyphNext,  px, dxgiPtr, dxgiFact2Ptr, ctxPtr, compInterop);
            _playBrush[0] = MakeGlyphBrush(GlyphPlay,  px, dxgiPtr, dxgiFact2Ptr, ctxPtr, compInterop);
            _playBrush[1] = MakeGlyphBrush(GlyphPause, px, dxgiPtr, dxgiFact2Ptr, ctxPtr, compInterop);
            _btnGlyph[BTN_PLAY].Brush = _isPlaying ? _playBrush[1] : _playBrush[0];
            _muteBrush[0] = MakeGlyphBrush(GlyphSnd,   px, dxgiPtr, dxgiFact2Ptr, ctxPtr, compInterop);
            _muteBrush[1] = MakeGlyphBrush(GlyphMute,  px, dxgiPtr, dxgiFact2Ptr, ctxPtr, compInterop);
            _btnGlyph[BTN_MUTE].Brush = _muted ? _muteBrush[1] : _muteBrush[0];

            // Slider volume icon — 4 brushes for vol levels (0=mute,1=low,2=mid,3=high)
            int iconPx = (int)Math.Ceiling(SliderIconSz * _scale);
            _sldIconBrush[0] = MakeGlyphBrush(GlyphMute, iconPx, dxgiPtr, dxgiFact2Ptr, ctxPtr, compInterop);
            _sldIconBrush[1] = MakeGlyphBrush(GlyphVol1, iconPx, dxgiPtr, dxgiFact2Ptr, ctxPtr, compInterop);
            _sldIconBrush[2] = MakeGlyphBrush(GlyphSnd,  iconPx, dxgiPtr, dxgiFact2Ptr, ctxPtr, compInterop);
            _sldIconBrush[3] = MakeGlyphBrush(GlyphVol3, iconPx, dxgiPtr, dxgiFact2Ptr, ctxPtr, compInterop);
            UpdateSliderIcon(_lastVol);

            // Volume % text swap chain
            _volTextPxW = (int)Math.Ceiling(SliderTextW * _scale);
            _volTextPxH = (int)Math.Ceiling(SliderIconSz * _scale);
            byte[] txtBgra = RenderTextBgra("--", _volTextPxW, _volTextPxH);
            var txtBrush = MakeBitmapBrush(txtBgra, _volTextPxW, _volTextPxH,
                dxgiPtr, dxgiFact2Ptr, ctxPtr, compInterop, out _volTextScPtr);
            if (_volTextVis != null) _volTextVis.Brush = txtBrush;

            // Keep ctxPtr alive for dynamic text updates; release factory/device
            _d2dCtxPtr = ctxPtr;
            Marshal.Release(dxgiFact2Ptr);
            Marshal.Release(dxgiPtr);
            Log("glyphs OK");
        }
        catch (Exception ex) { Log($"LoadGlyphs: {ex}"); }
    }

    unsafe CompositionSurfaceBrush MakeGlyphBrush(char glyph, int px,
        IntPtr dxgiPtr, IntPtr factPtr, IntPtr ctxPtr, ICompositorInterop compInterop)
        => MakeBitmapBrush(RenderGlyphBgra(glyph, px), px, px,
               dxgiPtr, factPtr, ctxPtr, compInterop, out _);

    unsafe CompositionSurfaceBrush MakeBitmapBrush(byte[] bgra, int w, int h,
        IntPtr dxgiPtr, IntPtr factPtr, IntPtr ctxPtr, ICompositorInterop compInterop,
        out IntPtr scPtrOut)
    {
        // Create swap chain for composition
        var desc = new DXGI_SWAP_CHAIN_DESC1
        {
            Width         = (uint)w,
            Height        = (uint)h,
            Format        = 87,
            Stereo        = 0,
            SampleCount   = 1,
            SampleQuality = 0,
            BufferUsage   = 0x20,
            BufferCount   = 2,
            Scaling       = 0,
            SwapEffect    = 3,
            AlphaMode     = 1,
            Flags         = 0,
        };
        IntPtr scPtr;
        int hr;
        {
            var vt = *(IntPtr**)factPtr;
            // IDXGIFactory2::CreateSwapChainForComposition  vtable[24]
            hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, DXGI_SWAP_CHAIN_DESC1*, IntPtr, IntPtr*, int>)vt[24])(
                factPtr, dxgiPtr, &desc, IntPtr.Zero, &scPtr);
        }
        if (hr != 0 || scPtr == IntPtr.Zero) throw new Exception($"CreateSwapChain 0x{hr:X8}");
        _swapChains.Add(scPtr);

        // GetBuffer(0) → IDXGISurface  vtable[9]
        var iidSurf = new Guid("cafcb56c-6ac3-4889-bf47-9e23bbd260ec");
        IntPtr dxgiSurfPtr;
        {
            var vt = *(IntPtr**)scPtr;
            hr = ((delegate* unmanaged[Stdcall]<IntPtr, uint, Guid*, IntPtr*, int>)vt[9])(scPtr, 0, &iidSurf, &dxgiSurfPtr);
        }
        if (hr != 0 || dxgiSurfPtr == IntPtr.Zero) throw new Exception($"GetBuffer 0x{hr:X8}");

        // CreateBitmapFromDxgiSurface → render-target bitmap  vtable[62]
        var bmpProps1 = new D2D1_BITMAP_PROPERTIES1
        {
            pixelFormat   = new D2D1_PIXEL_FORMAT { format = 87, alphaMode = 1 },
            dpiX          = 96f,
            dpiY          = 96f,
            bitmapOptions = 3,            // D2D1_BITMAP_OPTIONS_TARGET | CANNOT_DRAW
            colorContext  = IntPtr.Zero,
        };
        IntPtr bmp1Ptr;
        {
            var vt = *(IntPtr**)ctxPtr;
            hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, D2D1_BITMAP_PROPERTIES1*, IntPtr*, int>)vt[62])(
                ctxPtr, dxgiSurfPtr, &bmpProps1, &bmp1Ptr);
        }
        Marshal.Release(dxgiSurfPtr);
        if (hr != 0 || bmp1Ptr == IntPtr.Zero) throw new Exception($"BitmapFromDxgi 0x{hr:X8}");

        // SetTarget → BeginDraw → Clear → upload pixels → DrawBitmap → EndDraw → SetTarget(null)
        var vt2 = *(IntPtr**)ctxPtr;
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, void>)vt2[74])(ctxPtr, bmp1Ptr);  // SetTarget
        ((delegate* unmanaged[Stdcall]<IntPtr, void>)vt2[48])(ctxPtr);                   // BeginDraw
        var clr = new D2D1_COLOR_F { r = 0, g = 0, b = 0, a = 0 };
        ((delegate* unmanaged[Stdcall]<IntPtr, D2D1_COLOR_F*, void>)vt2[47])(ctxPtr, &clr); // Clear

        var bmpProps2 = new D2D1_BITMAP_PROPERTIES
        {
            pixelFormat = new D2D1_PIXEL_FORMAT { format = 87, alphaMode = 1 },
            dpiX = 96f, dpiY = 96f,
        };
        IntPtr bmpPtr;
        fixed (byte* pBgra = bgra)
        {
            hr = ((delegate* unmanaged[Stdcall]<IntPtr, D2D1_SIZE_U, void*, uint, D2D1_BITMAP_PROPERTIES*, IntPtr*, int>)vt2[4])(
                ctxPtr, new D2D1_SIZE_U { width = (uint)w, height = (uint)h },
                pBgra, (uint)(w * 4), &bmpProps2, &bmpPtr);
        }
        if (hr == 0 && bmpPtr != IntPtr.Zero)
        {
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, void*, float, int, void*, void>)vt2[26])(
                ctxPtr, bmpPtr, null, 1f, 0, null); // DrawBitmap
            Marshal.Release(bmpPtr);
        }

        hr = ((delegate* unmanaged[Stdcall]<IntPtr, ulong*, ulong*, int>)vt2[49])(ctxPtr, null, null); // EndDraw
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, void>)vt2[74])(ctxPtr, IntPtr.Zero);            // SetTarget(null)
        Marshal.Release(bmp1Ptr);
        if (hr != 0) throw new Exception($"EndDraw 0x{hr:X8}");

        // Present  vtable[8]
        {
            var vt = *(IntPtr**)scPtr;
            hr = ((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, int>)vt[8])(scPtr, 0, 0);
        }
        if (hr != 0) throw new Exception($"Present 0x{hr:X8}");

        // Wrap swap chain as ICompositionSurface
        hr = compInterop.CreateCompositionSurfaceForSwapChain(scPtr, out IntPtr compSurfPtr);
        if (hr != 0 || compSurfPtr == IntPtr.Zero) throw new Exception($"CompSurf 0x{hr:X8}");
        var compSurf = WinRT.MarshalInterface<ICompositionSurface>.FromAbi(compSurfPtr);
        Marshal.Release(compSurfPtr);

        var brush = _compositor.CreateSurfaceBrush(compSurf);
        brush.Stretch = CompositionStretch.Fill;
        scPtrOut = scPtr;
        return brush;
    }

    static byte[] RenderGlyphBgra(char glyph, int px)
    {
        using var bmp = new System.Drawing.Bitmap(px, px, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.Clear(System.Drawing.Color.Transparent);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using var font = new System.Drawing.Font("Segoe Fluent Icons", px * 0.60f, System.Drawing.GraphicsUnit.Pixel);
            g.DrawString(glyph.ToString(), font, System.Drawing.Brushes.White,
                new System.Drawing.RectangleF(0, 0, px, px),
                new System.Drawing.StringFormat
                {
                    Alignment     = System.Drawing.StringAlignment.Center,
                    LineAlignment = System.Drawing.StringAlignment.Center,
                });
        }
        // GDI+ Format32bppArgb = BGRA in memory, straight alpha → premultiply for D2D1
        var bits = bmp.LockBits(new System.Drawing.Rectangle(0, 0, px, px),
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        byte[] data = new byte[px * px * 4];
        Marshal.Copy(bits.Scan0, data, 0, data.Length);
        bmp.UnlockBits(bits);
        for (int i = 0; i < data.Length; i += 4)
        {
            byte a = data[i + 3];
            data[i]     = (byte)(data[i]     * a / 255);
            data[i + 1] = (byte)(data[i + 1] * a / 255);
            data[i + 2] = (byte)(data[i + 2] * a / 255);
        }
        return data;
    }

    static byte[] RenderTextBgra(string text, int w, int h)
    {
        using var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.Clear(System.Drawing.Color.Transparent);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using var font = new System.Drawing.Font("Segoe UI", h * 0.63f, System.Drawing.GraphicsUnit.Pixel);
            g.DrawString(text, font, System.Drawing.Brushes.White,
                new System.Drawing.RectangleF(0, 0, w, h),
                new System.Drawing.StringFormat
                {
                    Alignment     = System.Drawing.StringAlignment.Center,
                    LineAlignment = System.Drawing.StringAlignment.Center,
                });
        }
        var bits = bmp.LockBits(new System.Drawing.Rectangle(0, 0, w, h),
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        byte[] data = new byte[w * h * 4];
        Marshal.Copy(bits.Scan0, data, 0, data.Length);
        bmp.UnlockBits(bits);
        for (int i = 0; i < data.Length; i += 4)
        {
            byte a = data[i + 3];
            data[i]     = (byte)(data[i]     * a / 255);
            data[i + 1] = (byte)(data[i + 1] * a / 255);
            data[i + 2] = (byte)(data[i + 2] * a / 255);
        }
        return data;
    }

    unsafe void RenderBgraToSwapChain(IntPtr scPtr, byte[] bgra, int w, int h)
    {
        var iidSurf = new Guid("cafcb56c-6ac3-4889-bf47-9e23bbd260ec");
        IntPtr dxgiSurfPtr;
        int hr;
        {
            var vt = *(IntPtr**)scPtr;
            hr = ((delegate* unmanaged[Stdcall]<IntPtr, uint, Guid*, IntPtr*, int>)vt[9])(scPtr, 0, &iidSurf, &dxgiSurfPtr);
        }
        if (hr != 0 || dxgiSurfPtr == IntPtr.Zero) return;

        var bp1 = new D2D1_BITMAP_PROPERTIES1
        {
            pixelFormat = new D2D1_PIXEL_FORMAT { format = 87, alphaMode = 1 },
            dpiX = 96f, dpiY = 96f, bitmapOptions = 3, colorContext = IntPtr.Zero,
        };
        IntPtr bmp1Ptr;
        {
            var vt = *(IntPtr**)_d2dCtxPtr;
            hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, D2D1_BITMAP_PROPERTIES1*, IntPtr*, int>)vt[62])(
                _d2dCtxPtr, dxgiSurfPtr, &bp1, &bmp1Ptr);
        }
        Marshal.Release(dxgiSurfPtr);
        if (hr != 0 || bmp1Ptr == IntPtr.Zero) return;

        var vt2 = *(IntPtr**)_d2dCtxPtr;
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, void>)vt2[74])(_d2dCtxPtr, bmp1Ptr);
        ((delegate* unmanaged[Stdcall]<IntPtr, void>)vt2[48])(_d2dCtxPtr);
        var clr = new D2D1_COLOR_F { r = 0, g = 0, b = 0, a = 0 };
        ((delegate* unmanaged[Stdcall]<IntPtr, D2D1_COLOR_F*, void>)vt2[47])(_d2dCtxPtr, &clr);

        var bp2 = new D2D1_BITMAP_PROPERTIES
        {
            pixelFormat = new D2D1_PIXEL_FORMAT { format = 87, alphaMode = 1 }, dpiX = 96f, dpiY = 96f,
        };
        IntPtr bmpPtr;
        fixed (byte* pBgra = bgra)
        {
            hr = ((delegate* unmanaged[Stdcall]<IntPtr, D2D1_SIZE_U, void*, uint, D2D1_BITMAP_PROPERTIES*, IntPtr*, int>)vt2[4])(
                _d2dCtxPtr, new D2D1_SIZE_U { width = (uint)w, height = (uint)h },
                pBgra, (uint)(w * 4), &bp2, &bmpPtr);
        }
        if (hr == 0 && bmpPtr != IntPtr.Zero)
        {
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, void*, float, int, void*, void>)vt2[26])(
                _d2dCtxPtr, bmpPtr, null, 1f, 0, null);
            Marshal.Release(bmpPtr);
        }
        ((delegate* unmanaged[Stdcall]<IntPtr, ulong*, ulong*, int>)vt2[49])(_d2dCtxPtr, null, null);
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, void>)vt2[74])(_d2dCtxPtr, IntPtr.Zero);
        Marshal.Release(bmp1Ptr);

        var vt3 = *(IntPtr**)scPtr;
        ((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, int>)vt3[8])(scPtr, 0, 0);
    }

    void UpdateVolText(float vol)
    {
        if (_d2dCtxPtr == IntPtr.Zero || _volTextScPtr == IntPtr.Zero) return;
        try
        {
            string text = $"{(int)Math.Round(vol * 100)}%";
            byte[] bgra = RenderTextBgra(text, _volTextPxW, _volTextPxH);
            RenderBgraToSwapChain(_volTextScPtr, bgra, _volTextPxW, _volTextPxH);
        }
        catch (Exception ex) { Log($"VolText: {ex.Message}"); }
    }

    void UpdateSliderIcon(float vol)
    {
        if (_volSliderIcon == null) return;
        int idx = (_muted || vol <= 0.01f) ? 0 : vol <= 0.33f ? 1 : vol <= 0.66f ? 2 : 3;
        var b = _sldIconBrush[idx];
        if (b != null) _volSliderIcon.Brush = b;
    }

    // ── Visual states ─────────────────────────────────────────────────────────

    void RefreshBtnVisual(int i)
    {
        bool h = _hovered == i, p = _pressed == i;
        _btnBgBrush[i].Color = i switch
        {
            BTN_MUTE when _muted    => p ? C(80, 255, 70, 70) : h ? C(50, 255, 70, 70) : C(30, 255, 70, 70),
            BTN_PLAY when _isPlaying => p ? C(80, 60, 220,100) : h ? C(50, 60, 220,100) : C(30, 60, 220,100),
            _ => p ? C(60, 255, 255, 255) : h ? C(28, 255, 255, 255) : C(0, 255, 255, 255),
        };

        // Swap glyph for stateful buttons
        CompositionSurfaceBrush? gb = i switch
        {
            BTN_PLAY => _isPlaying ? _playBrush[1] : _playBrush[0],
            BTN_MUTE => _muted     ? _muteBrush[1] : _muteBrush[0],
            _        => null,
        };
        if (gb != null) _btnGlyph[i].Brush = gb;

        if (i == BTN_MUTE) UpdateSliderIcon(_lastVol);
    }

    void UpdateVolFill(float vol)
    {
        if (_volFill == null || _volFillBrush == null) return;
        float maxW = (SliderW - 4) * _scale;
        _volFill.Size       = new Vector2(maxW * Math.Clamp(vol, 0f, 1f), _volFill.Size.Y);
        _volFillBrush.Color = _muted ? C(80, 255, 90, 90) : C(100, 255, 255, 255);
        _lastVol = vol;
        UpdateVolText(vol);
        UpdateSliderIcon(vol);
    }

    // ── Hit testing ───────────────────────────────────────────────────────────

    int HitTest(int px, int py)
    {
        if (py < 0 || py >= _physH) return -1;
        for (int i = 0; i < 4; i++)
        {
            int cx = (int)(BtnX[i] * _scale);
            int cw = (int)(LogicalHeight * _scale);
            if (px >= cx && px < cx + cw) return i;
        }
        int sx = (int)(SliderX * _scale), sw = (int)(SliderW * _scale);
        if (px >= sx && px < sx + sw) return 4;
        return -1;
    }

    // ── Mouse handlers ────────────────────────────────────────────────────────

    static (int x, int y) Pos(nint lp)
        => ((int)(short)(lp & 0xFFFF), (int)(short)((lp >> 16) & 0xFFFF));

    void OnMouseMove(int px, int py)
    {
        if (!_mouseTracking)
        {
            var tme = new TRACKMOUSEEVENT
            {
                cbSize    = (uint)Marshal.SizeOf<TRACKMOUSEEVENT>(),
                dwFlags   = TME_LEAVE,
                hwndTrack = _hwnd,
            };
            TrackMouseEvent(ref tme);
            _mouseTracking = true;
        }

        if (_dragging) { SeekVolume(px); return; }

        int hit = HitTest(px, py);
        if (hit == _hovered) return;
        int prev = _hovered;
        _hovered = hit;
        if (prev  is >= 0 and <= 3) RefreshBtnVisual(prev);
        if (_hovered is >= 0 and <= 3) RefreshBtnVisual(_hovered);
    }

    void OnMouseDown(int px, int py)
    {
        int hit = HitTest(px, py);
        _pressed = hit;
        if (hit is >= 0 and <= 3) RefreshBtnVisual(hit);

        if (hit == 4 && _volSvc != null)
        {
            _dragging = true;
            SeekVolume(px);
        }
        if (hit >= 0) SetCapture(_hwnd);
    }

    void OnMouseUp(int px, int py)
    {
        ReleaseCapture();
        bool wasDrag = _dragging;
        _dragging = false;

        if (!wasDrag && _pressed >= 0 && _pressed == HitTest(px, py))
            FireButton(_pressed);

        int old = _pressed;
        _pressed = -1;
        if (old is >= 0 and <= 3) RefreshBtnVisual(old);
    }

    void OnMouseLeave()
    {
        _mouseTracking = false;
        _hovered       = -1;
        for (int i = 0; i < 4; i++) RefreshBtnVisual(i);
    }

    void SeekVolume(int px)
    {
        if (_volSvc == null || _volFill == null) return;
        int sx = (int)(SliderX * _scale), sw = (int)(SliderW * _scale);
        float vol = Math.Clamp((float)(px - sx) / sw, 0f, 1f);
        _volSvc.SetVolume(vol);
        UpdateVolFill(vol);
    }

    void FireButton(int btn)
    {
        switch (btn)
        {
            case BTN_PREV: MediaKeyService.Send(MediaKey.PrevTrack); break;
            case BTN_PLAY: MediaKeyService.Send(MediaKey.PlayPause); break;
            case BTN_NEXT: MediaKeyService.Send(MediaKey.NextTrack); break;
            case BTN_MUTE:
                _volSvc?.ToggleMute();
                _muted = _volSvc?.GetMute() ?? false;
                RefreshBtnVisual(BTN_MUTE);
                UpdateVolFill(_volSvc?.GetVolume() ?? 0f);
                break;
        }
    }

    // ── Cross-thread sync ─────────────────────────────────────────────────────

    void SyncVolume()
    {
        if (_volSvc == null) return;
        try
        {
            bool  muted = _volSvc.GetMute();
            float vol   = _volSvc.GetVolume();
            if (muted != _muted) { _muted = muted; RefreshBtnVisual(BTN_MUTE); }
            UpdateVolFill(vol);
        }
        catch { }
    }

    void SyncPlayState()
    {
        bool playing = _wasapi?.IsPlaying ?? false;
        if (playing == _isPlaying) return;
        _isPlaying = playing;
        RefreshBtnVisual(BTN_PLAY);
    }

    // ── WndProc ───────────────────────────────────────────────────────────────

    static nint WndProc(IntPtr hWnd, uint msg, nuint wp, nint lp)
    {
        var inst = s_inst;
        if (inst != null)
        {
            switch (msg)
            {
                case WM_MOUSEMOVE:   { var (x,y) = Pos(lp); inst.OnMouseMove(x, y); break; }
                case WM_LBUTTONDOWN: { var (x,y) = Pos(lp); inst.OnMouseDown(x, y); break; }
                case WM_LBUTTONUP:   { var (x,y) = Pos(lp); inst.OnMouseUp(x, y);   break; }
                case WM_MOUSELEAVE:  inst.OnMouseLeave(); break;
                case WM_PLAY_STATE:  inst.SyncPlayState(); return 0;
                case WM_VOL_SYNC:    inst.SyncVolume();    return 0;
            }
        }
        return DefWindowProc(hWnd, msg, wp, lp);
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        s_inst = null;
        _volTimer?.Dispose();
        _wasapi?.Dispose();
        _volSvc?.Dispose();
        _target.Root = null;
        foreach (var sc in _swapChains) Marshal.Release(sc);
        if (_d2dCtxPtr != IntPtr.Zero) { Marshal.Release(_d2dCtxPtr); _d2dCtxPtr = IntPtr.Zero; }
        if (_hwnd != IntPtr.Zero) DestroyWindow(_hwnd);
    }
}
