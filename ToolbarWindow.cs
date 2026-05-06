using System.Numerics;
using System.Runtime.InteropServices;
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

    [DllImport("user32.dll")] static extern bool   DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, nuint wp, nint lp);
    [DllImport("user32.dll")] static extern IntPtr FindWindow(string cls, string? title);
    [DllImport("user32.dll")] static extern bool   GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] static extern bool   SetWindowPos(IntPtr hWnd, IntPtr hWndAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern IntPtr SetParent(IntPtr hWnd, IntPtr hWndNew);
    [DllImport("user32.dll")] static extern int    SetWindowLong(IntPtr hWnd, int nIndex, uint newLong);
    [DllImport("user32.dll")] static extern uint   GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] static extern IntPtr LoadCursor(IntPtr hInstance, int cursor);
    [DllImport("user32.dll")] static extern bool   TrackMouseEvent(ref TRACKMOUSEEVENT tme);
    [DllImport("user32.dll")] static extern bool   PostMessage(IntPtr hWnd, uint msg, nuint wp, nint lp);
    [DllImport("user32.dll")] static extern IntPtr SetCapture(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool   ReleaseCapture();
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string? n);

    [DllImport("CoreMessaging.dll")]
    static extern int CreateDispatcherQueueController(
        DispatcherQueueOptions o,
        [MarshalAs(UnmanagedType.IUnknown)] out object result);

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    static extern int WindowsCreateString(string s, int len, out IntPtr hstring);

    [DllImport("combase.dll")]
    static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll")]
    static extern int RoActivateInstance(IntPtr classId, out IntPtr instance);

    [DllImport("d3d11.dll")]
    static extern int D3D11CreateDevice(
        IntPtr pAdapter, int DriverType, IntPtr Software, uint Flags,
        IntPtr pFeatureLevels, uint FeatureLevels, uint SDKVersion,
        out IntPtr ppDevice, IntPtr pFeatureLevel, IntPtr ppImmediateContext);

    [DllImport("d2d1.dll")]
    static extern int D2D1CreateFactory(
        int factoryType, ref Guid riid, IntPtr pOptions, out IntPtr ppFactory);

    // ── COM interfaces ────────────────────────────────────────────────────────

    [ComImport, Guid("29E691FA-4567-4DCA-B319-D0F207EB6807")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICompositorDesktopInterop
    {
        [PreserveSig] int CreateDesktopWindowTarget(IntPtr hwnd, bool topmost, out IntPtr result);
        [PreserveSig] int EnsureOnThread(uint tid);
    }

    [ComImport, Guid("25297D5C-3AD4-4C9C-B5CF-E36A38512330")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICompositorInterop
    {
        [PreserveSig] int CreateCompositionSurfaceForHandle(IntPtr handle, out IntPtr result);
        [PreserveSig] int CreateCompositionSurfaceForSwapChain(IntPtr swapChain, out IntPtr result);
        [PreserveSig] int CreateGraphicsDevice(IntPtr renderingDevice, out IntPtr result);
    }

    // ── WinRT GUIDs ───────────────────────────────────────────────────────────

    static readonly Guid IID_IInspectable              = new("AF86E2E0-B12D-4C6A-9C5A-D7AA65101E90");
    static readonly Guid IID_ICompositor               = new("B403CA50-7F8C-4E83-985F-CC45060036D8");
    static readonly Guid IID_ICompositorDesktopInterop = new("29E691FA-4567-4DCA-B319-D0F207EB6807");
    static readonly Guid IID_ICompositorInterop        = new("25297D5C-3AD4-4C9C-B5CF-E36A38512330");
    static readonly Guid IID_IVisual                   = new("117E202D-A859-4C89-873B-C2AA566788E3");
    static readonly Guid IID_IVisual2                  = new("3052B611-56C3-4C3E-8BF3-F6E1AD473F06");
    static readonly Guid IID_ISpriteVisual             = new("08E05581-1AD1-4F97-9757-402D76E4233B");
    static readonly Guid IID_ICompositionSurfaceBrush  = new("AD016D79-1E4C-4C0D-9C29-83338C87C162");
    static readonly Guid IID_ICompositionTarget        = new("A1BEA8BA-D726-4663-8129-6B5E7927FFA6");

    // ── WinRT vtable helpers ──────────────────────────────────────────────────

    static IntPtr Qi(IntPtr obj, Guid iid)
    { Marshal.QueryInterface(obj, ref iid, out IntPtr p); return p; }

    static IntPtr ToInsp(IntPtr obj) => Qi(obj, IID_IInspectable);

    static IntPtr ActivateWinRT(string className)
    {
        WindowsCreateString(className, className.Length, out IntPtr hs);
        RoActivateInstance(hs, out IntPtr obj);
        WindowsDeleteString(hs);
        return obj;
    }

    // ICompositor [22] CreateSpriteVisual, [24] CreateSurfaceBrush
    static unsafe IntPtr CompCreateSpriteVisual(IntPtr c)
    { IntPtr r; ((delegate* unmanaged[Stdcall]<IntPtr,IntPtr*,int>)(*(IntPtr**)c)[22])(c,&r); return r; }

    static unsafe IntPtr CompCreateSurfaceBrush(IntPtr c, IntPtr surf)
    { IntPtr r; ((delegate* unmanaged[Stdcall]<IntPtr,IntPtr,IntPtr*,int>)(*(IntPtr**)c)[24])(c,surf,&r); return r; }

    // IVisual2 [11] set_RelativeSizeAdjustment(Vector2)
    static unsafe void Vis2SetRelSize(IntPtr v, Vector2 val)
    { ((delegate* unmanaged[Stdcall]<IntPtr,Vector2,int>)(*(IntPtr**)v)[11])(v,val); }

    // ISpriteVisual [7] set_Brush(IInspectable*)
    static unsafe void SprSetBrush(IntPtr v, IntPtr brushInsp)
    { ((delegate* unmanaged[Stdcall]<IntPtr,IntPtr,int>)(*(IntPtr**)v)[7])(v,brushInsp); }

    // ICompositionTarget [7] set_Root(IVisual*)
    static unsafe void TargetSetRoot(IntPtr t, IntPtr vis)
    { ((delegate* unmanaged[Stdcall]<IntPtr,IntPtr,int>)(*(IntPtr**)t)[7])(t,vis); }

    // ICompositionSurfaceBrush [11] set_Stretch(int)  Fill=2
    static unsafe void SurfBrushSetStretch(IntPtr b, int stretch)
    { ((delegate* unmanaged[Stdcall]<IntPtr,int,int>)(*(IntPtr**)b)[11])(b,stretch); }

    // ── Structs ───────────────────────────────────────────────────────────────

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
    [StructLayout(LayoutKind.Sequential)] struct TRACKMOUSEEVENT { public uint cbSize, dwFlags; public IntPtr hwndTrack; public uint dwHoverTime; }

    [StructLayout(LayoutKind.Sequential)] struct D2D1_SIZE_U  { public uint width, height; }
    [StructLayout(LayoutKind.Sequential)] struct D2D1_PIXEL_FORMAT { public int format, alphaMode; }
    [StructLayout(LayoutKind.Sequential)] struct D2D1_BITMAP_PROPERTIES  { public D2D1_PIXEL_FORMAT pixelFormat; public float dpiX, dpiY; }
    [StructLayout(LayoutKind.Sequential)] struct D2D1_COLOR_F { public float r, g, b, a; }

    [StructLayout(LayoutKind.Sequential)]
    struct DXGI_SWAP_CHAIN_DESC1
    {
        public uint Width, Height;
        public int  Format, Stereo;
        public uint SampleCount, SampleQuality, BufferUsage, BufferCount;
        public int  Scaling, SwapEffect, AlphaMode;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct D2D1_BITMAP_PROPERTIES1
    {
        public D2D1_PIXEL_FORMAT pixelFormat;
        public float  dpiX, dpiY;
        public int    bitmapOptions;
        private int   _pad;
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

    const int LogicalWidth  = 399;
    const int LogicalHeight = 52;

    static readonly int[] BtnX = [4, 58, 112, 189];
    const int SliderX = 243, SliderW = 156;
    const int Pad = 6, BtnVis = 40;
    const float Corner = 6f;

    const int BTN_PREV = 0, BTN_PLAY = 1, BTN_NEXT = 2, BTN_MUTE = 3;

    const int SliderIconSz = 24;
    const int SliderTextW  = 38;

    const char GlyphPrev  = '';
    const char GlyphPlay  = '';
    const char GlyphPause = '';
    const char GlyphNext  = '';
    const char GlyphVol1  = '';
    const char GlyphSnd   = '';
    const char GlyphVol3  = '';
    const char GlyphMute  = '';

    const string WndClass = "WinToolbarMB";

    // ── Fields ────────────────────────────────────────────────────────────────

    static WndProcDelegate? s_proc;
    static ToolbarWindow?   s_inst;

    readonly object _dqc;
    IntPtr _compositorRaw;
    IntPtr _compositor;
    IntPtr _target;
    IntPtr _rootSprVis;   // ISpriteVisual* — single root, brush set in LoadGlyphs
    readonly IntPtr _hwnd;

    float _scale;
    int   _physH;
    int   _pxW;

    VolumeEndpointService?  _volSvc;
    WasapiMonitorService?   _wasapi;
    System.Threading.Timer? _volTimer;

    IntPtr _toolbarSc;    // full-toolbar DXGI swap chain
    IntPtr _d2dCtxPtr;    // ID2D1DeviceContext*
    readonly List<IntPtr> _swapChains = new();

    float _lastVol;
    bool  _mouseTracking;
    int   _hovered = -1;
    int   _pressed = -1;
    bool  _dragging;
    bool  _muted;
    bool  _isPlaying;
    bool  _disposed;

    // ── Constructor ───────────────────────────────────────────────────────────

    public ToolbarWindow()
    {
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
        _pxW   = (int)Math.Ceiling(LogicalWidth * _scale) + 1;

        _hwnd = CreateWindowEx(
            WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, WndClass, null,
            WS_POPUP | WS_VISIBLE | WS_CLIPCHILDREN | WS_CLIPSIBLINGS,
            tb.Left, tb.Top, _pxW, _physH,
            IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastWin32Error()}");

        _compositorRaw = ActivateWinRT("Windows.UI.Composition.Compositor");
        _compositor    = Qi(_compositorRaw, IID_ICompositor);

        var iidDI = IID_ICompositorDesktopInterop;
        Marshal.QueryInterface(_compositorRaw, ref iidDI, out IntPtr diPtr);
        var desktopInterop = (ICompositorDesktopInterop)Marshal.GetObjectForIUnknown(diPtr);
        Marshal.Release(diPtr);

        int hr = desktopInterop.CreateDesktopWindowTarget(_hwnd, true, out IntPtr targetPtr);
        if (hr != 0 || targetPtr == IntPtr.Zero)
            throw new InvalidOperationException($"CreateDesktopWindowTarget hr=0x{hr:X8}");
        _target = Qi(targetPtr, IID_ICompositionTarget);
        Marshal.Release(targetPtr);

        BuildUI();

        SetParent(_hwnd, taskbar);
        uint style = GetWindowLong(_hwnd, GWL_STYLE);
        SetWindowLong(_hwnd, GWL_STYLE, (style & ~WS_POPUP) | WS_CHILD);
        SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, _pxW, _physH, SWP_NOACTIVATE | SWP_FRAMECHANGED);

        s_inst = this;

        try
        {
            _volSvc  = new VolumeEndpointService();
            _muted   = _volSvc.GetMute();
            _lastVol = _volSvc.GetVolume();
        }
        catch { }

        try
        {
            _wasapi = new WasapiMonitorService();
            _wasapi.StateChanged += () => PostMessage(_hwnd, WM_PLAY_STATE, 0, 0);
        }
        catch { }

        _volTimer = new System.Threading.Timer(
            _ => PostMessage(_hwnd, WM_VOL_SYNC, 0, 0), null, 300, 300);

        LoadGlyphs();
    }

    // ── UI build ──────────────────────────────────────────────────────────────

    void BuildUI()
    {
        // Single SpriteVisual as root — no ContainerVisual, no child collection ops
        IntPtr rootSpr  = CompCreateSpriteVisual(_compositor);
        IntPtr rootVis2 = Qi(rootSpr, IID_IVisual2);
        Vis2SetRelSize(rootVis2, Vector2.One);
        Marshal.Release(rootVis2);
        IntPtr rootVis = Qi(rootSpr, IID_IVisual);
        TargetSetRoot(_target, rootVis);
        Marshal.Release(rootVis);
        _rootSprVis = Qi(rootSpr, IID_ISpriteVisual);
        Marshal.Release(rootSpr);
    }

    // ── Surface setup ─────────────────────────────────────────────────────────

    void LoadGlyphs()
    {
        try
        {
            const uint BGRA = 0x20;
            int hr = D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, BGRA,
                IntPtr.Zero, 0, 7, out IntPtr d3dPtr, IntPtr.Zero, IntPtr.Zero);
            if (hr != 0 || d3dPtr == IntPtr.Zero)
                hr = D3D11CreateDevice(IntPtr.Zero, 5, IntPtr.Zero, BGRA,
                    IntPtr.Zero, 0, 7, out d3dPtr, IntPtr.Zero, IntPtr.Zero);
            if (d3dPtr == IntPtr.Zero) throw new Exception($"D3D11 hr=0x{hr:X8}");

            var iidDxgi = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
            hr = Marshal.QueryInterface(d3dPtr, ref iidDxgi, out IntPtr dxgiPtr);
            Marshal.Release(d3dPtr);
            if (hr != 0 || dxgiPtr == IntPtr.Zero) throw new Exception($"IDXGIDevice hr=0x{hr:X8}");

            var iidFact1 = new Guid("bb12d362-daee-4b9a-aa1d-14ba401cfa1f");
            hr = D2D1CreateFactory(0, ref iidFact1, IntPtr.Zero, out IntPtr factPtr);
            if (hr != 0 || factPtr == IntPtr.Zero) { Marshal.Release(dxgiPtr); throw new Exception($"D2D1Factory1 hr=0x{hr:X8}"); }

            IntPtr d2dDevPtr;
            unsafe { var vt = *(IntPtr**)factPtr; hr = ((delegate* unmanaged[Stdcall]<IntPtr,IntPtr,IntPtr*,int>)vt[17])(factPtr,dxgiPtr,&d2dDevPtr); }
            Marshal.Release(factPtr);
            if (hr != 0 || d2dDevPtr == IntPtr.Zero) { Marshal.Release(dxgiPtr); throw new Exception($"ID2D1Device hr=0x{hr:X8}"); }

            IntPtr ctxPtr;
            unsafe { var vt = *(IntPtr**)d2dDevPtr; hr = ((delegate* unmanaged[Stdcall]<IntPtr,int,IntPtr*,int>)vt[4])(d2dDevPtr,0,&ctxPtr); }
            Marshal.Release(d2dDevPtr);
            if (hr != 0 || ctxPtr == IntPtr.Zero) { Marshal.Release(dxgiPtr); throw new Exception($"ID2D1DevCtx hr=0x{hr:X8}"); }

            IntPtr adapterPtr;
            unsafe { var vt = *(IntPtr**)dxgiPtr; hr = ((delegate* unmanaged[Stdcall]<IntPtr,IntPtr*,int>)vt[7])(dxgiPtr,&adapterPtr); }
            if (hr != 0 || adapterPtr == IntPtr.Zero)
            { Marshal.Release(ctxPtr); Marshal.Release(dxgiPtr); throw new Exception($"GetAdapter hr=0x{hr:X8}"); }

            var iidFact2 = new Guid("50c83a1c-e072-4c48-87b0-3630fa36a6d0");
            IntPtr dxgiFact2Ptr;
            unsafe { var vt = *(IntPtr**)adapterPtr; hr = ((delegate* unmanaged[Stdcall]<IntPtr,Guid*,IntPtr*,int>)vt[6])(adapterPtr,&iidFact2,&dxgiFact2Ptr); }
            Marshal.Release(adapterPtr);
            if (hr != 0 || dxgiFact2Ptr == IntPtr.Zero)
            { Marshal.Release(ctxPtr); Marshal.Release(dxgiPtr); throw new Exception($"IDXGIFactory2 hr=0x{hr:X8}"); }

            var desc = new DXGI_SWAP_CHAIN_DESC1
            {
                Width = (uint)_pxW, Height = (uint)_physH,
                Format = 87, Stereo = 0, SampleCount = 1, SampleQuality = 0,
                BufferUsage = 0x20, BufferCount = 2,
                Scaling = 0, SwapEffect = 3, AlphaMode = 1, Flags = 0,
            };
            IntPtr scPtr;
            unsafe
            {
                var vt = *(IntPtr**)dxgiFact2Ptr;
                hr = ((delegate* unmanaged[Stdcall]<IntPtr,IntPtr,DXGI_SWAP_CHAIN_DESC1*,IntPtr,IntPtr*,int>)vt[24])(
                    dxgiFact2Ptr, dxgiPtr, &desc, IntPtr.Zero, &scPtr);
            }
            Marshal.Release(dxgiFact2Ptr);
            Marshal.Release(dxgiPtr);
            if (hr != 0 || scPtr == IntPtr.Zero) throw new Exception($"CreateSwapChain 0x{hr:X8}");
            _swapChains.Add(scPtr);
            _toolbarSc = scPtr;

            var iidCI = IID_ICompositorInterop;
            Marshal.QueryInterface(_compositorRaw, ref iidCI, out IntPtr ciPtr);
            var compInterop = (ICompositorInterop)Marshal.GetObjectForIUnknown(ciPtr);
            Marshal.Release(ciPtr);

            hr = compInterop.CreateCompositionSurfaceForSwapChain(scPtr, out IntPtr compSurfPtr);
            if (hr != 0 || compSurfPtr == IntPtr.Zero) throw new Exception($"CompSurf 0x{hr:X8}");

            IntPtr surfBrushPtr = CompCreateSurfaceBrush(_compositor, compSurfPtr);
            Marshal.Release(compSurfPtr);

            IntPtr iSurfBrush = Qi(surfBrushPtr, IID_ICompositionSurfaceBrush);
            SurfBrushSetStretch(iSurfBrush, 2);
            Marshal.Release(iSurfBrush);

            IntPtr brushInsp = ToInsp(surfBrushPtr);
            Marshal.Release(surfBrushPtr);
            SprSetBrush(_rootSprVis, brushInsp);
            Marshal.Release(brushInsp);

            _d2dCtxPtr = ctxPtr;
            Render();
        }
        catch { }
    }

    // ── Rendering ─────────────────────────────────────────────────────────────

    void Render()
    {
        if (_toolbarSc == IntPtr.Zero || _d2dCtxPtr == IntPtr.Zero) return;
        try
        {
            byte[] bgra = RenderToolbarBgra();
            RenderBgraToSwapChain(_toolbarSc, bgra, _pxW, _physH);
        }
        catch { }
    }

    byte[] RenderToolbarBgra()
    {
        float P(float l) => l * _scale;

        using var bmp = new System.Drawing.Bitmap(_pxW, _physH, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode     = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            g.Clear(System.Drawing.Color.FromArgb(255, 0x1C, 0x1C, 0x1C));

            // Button overlays
            for (int i = 0; i < 4; i++)
            {
                bool h = _hovered == i, p = _pressed == i;
                var oc = i switch
                {
                    BTN_MUTE when _muted     => p ? System.Drawing.Color.FromArgb(80,255,70,70)   : h ? System.Drawing.Color.FromArgb(50,255,70,70)   : System.Drawing.Color.FromArgb(30,255,70,70),
                    BTN_PLAY when _isPlaying => p ? System.Drawing.Color.FromArgb(80,60,220,100)  : h ? System.Drawing.Color.FromArgb(50,60,220,100)  : System.Drawing.Color.FromArgb(30,60,220,100),
                    _                        => p ? System.Drawing.Color.FromArgb(60,255,255,255) : h ? System.Drawing.Color.FromArgb(28,255,255,255) : System.Drawing.Color.FromArgb(0,0,0,0),
                };
                if (oc.A > 0)
                {
                    float bx = P(BtnX[i] + Pad), by = P(Pad), bw = P(BtnVis), bh = P(BtnVis), cr = P(Corner);
                    using var gp = RoundedRect(bx, by, bw, bh, cr);
                    using var br = new System.Drawing.SolidBrush(oc);
                    g.FillPath(br, gp);
                }
            }

            // Button glyphs
            var centered = new System.Drawing.StringFormat
            {
                Alignment     = System.Drawing.StringAlignment.Center,
                LineAlignment = System.Drawing.StringAlignment.Center,
            };
            using var glyphFont = new System.Drawing.Font("Segoe Fluent Icons", P(BtnVis) * 0.60f, System.Drawing.GraphicsUnit.Pixel);
            string[] glyphs =
            [
                GlyphPrev.ToString(),
                (_isPlaying ? GlyphPause : GlyphPlay).ToString(),
                GlyphNext.ToString(),
                (_muted     ? GlyphMute  : GlyphSnd).ToString(),
            ];
            for (int i = 0; i < 4; i++)
                g.DrawString(glyphs[i], glyphFont, System.Drawing.Brushes.White,
                    new System.Drawing.RectangleF(P(BtnX[i] + Pad), P(Pad), P(BtnVis), P(BtnVis)), centered);

            // Separator
            using (var sepBr = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(50, 255, 255, 255)))
                g.FillRectangle(sepBr, P(167), P(10), P(1), P(32));

            // Slider background
            {
                using var gp = RoundedRect(P(SliderX), P(Pad), P(SliderW), P(BtnVis), P(Corner));
                using var br = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(35, 255, 255, 255));
                g.FillPath(br, gp);
            }

            // Volume fill
            {
                float fillW = (SliderW - 4) * _scale * Math.Clamp(_lastVol, 0f, 1f);
                if (fillW >= 1f)
                {
                    var fc = _muted
                        ? System.Drawing.Color.FromArgb(80,  255, 90,  90)
                        : System.Drawing.Color.FromArgb(100, 255, 255, 255);
                    using var br = new System.Drawing.SolidBrush(fc);
                    g.FillRectangle(br, P(SliderX + 2), P(Pad + 2), fillW, P(BtnVis - 4));
                }
            }

            // Slider icon + % text
            {
                int sldBlockX  = SliderX + SliderW / 2 - (SliderIconSz + 2 + SliderTextW) / 2;
                float sldIconY = Pad + (BtnVis - SliderIconSz) / 2f;
                int idx = (_muted || _lastVol <= 0.01f) ? 0 : _lastVol <= 0.33f ? 1 : _lastVol <= 0.66f ? 2 : 3;
                char sg = idx switch { 1 => GlyphVol1, 2 => GlyphSnd, 3 => GlyphVol3, _ => GlyphMute };
                float sz = P(SliderIconSz);
                using var iconFont = new System.Drawing.Font("Segoe Fluent Icons", sz * 0.80f, System.Drawing.GraphicsUnit.Pixel);
                g.DrawString(sg.ToString(), iconFont, System.Drawing.Brushes.White,
                    new System.Drawing.RectangleF(P(sldBlockX), P(sldIconY), sz, sz), centered);

                float tw = P(SliderTextW), th = P(SliderIconSz);
                using var txtFont = new System.Drawing.Font("Segoe UI", th * 0.63f, System.Drawing.GraphicsUnit.Pixel);
                g.DrawString($"{(int)Math.Round(_lastVol * 100)}%", txtFont, System.Drawing.Brushes.White,
                    new System.Drawing.RectangleF(P(sldBlockX + SliderIconSz + 2), P(sldIconY), tw, th), centered);
            }
        }

        var bits = bmp.LockBits(new System.Drawing.Rectangle(0, 0, _pxW, _physH),
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        byte[] data = new byte[_pxW * _physH * 4];
        Marshal.Copy(bits.Scan0, data, 0, data.Length);
        bmp.UnlockBits(bits);
        for (int i = 0; i < data.Length; i += 4)
        {
            byte a = data[i + 3];
            if (a < 255)
            {
                data[i]     = (byte)(data[i]     * a / 255);
                data[i + 1] = (byte)(data[i + 1] * a / 255);
                data[i + 2] = (byte)(data[i + 2] * a / 255);
            }
        }
        return data;
    }

    static System.Drawing.Drawing2D.GraphicsPath RoundedRect(float x, float y, float w, float h, float r)
    {
        var gp = new System.Drawing.Drawing2D.GraphicsPath();
        gp.AddArc(x,           y,           r*2, r*2, 180, 90);
        gp.AddArc(x + w - r*2, y,           r*2, r*2, 270, 90);
        gp.AddArc(x + w - r*2, y + h - r*2, r*2, r*2,   0, 90);
        gp.AddArc(x,           y + h - r*2, r*2, r*2,  90, 90);
        gp.CloseFigure();
        return gp;
    }

    unsafe void RenderBgraToSwapChain(IntPtr scPtr, byte[] bgra, int w, int h)
    {
        var iidSurf = new Guid("cafcb56c-6ac3-4889-bf47-9e23bbd260ec");
        IntPtr dxgiSurfPtr;
        int hr;
        { var vt = *(IntPtr**)scPtr; hr = ((delegate* unmanaged[Stdcall]<IntPtr,uint,Guid*,IntPtr*,int>)vt[9])(scPtr,0,&iidSurf,&dxgiSurfPtr); }
        if (hr != 0 || dxgiSurfPtr == IntPtr.Zero) return;

        var bp1 = new D2D1_BITMAP_PROPERTIES1
        {
            pixelFormat = new D2D1_PIXEL_FORMAT { format = 87, alphaMode = 1 },
            dpiX = 96f, dpiY = 96f, bitmapOptions = 3, colorContext = IntPtr.Zero,
        };
        IntPtr bmp1Ptr;
        { var vt = *(IntPtr**)_d2dCtxPtr; hr = ((delegate* unmanaged[Stdcall]<IntPtr,IntPtr,D2D1_BITMAP_PROPERTIES1*,IntPtr*,int>)vt[62])(_d2dCtxPtr,dxgiSurfPtr,&bp1,&bmp1Ptr); }
        Marshal.Release(dxgiSurfPtr);
        if (hr != 0 || bmp1Ptr == IntPtr.Zero) return;

        var vt2 = *(IntPtr**)_d2dCtxPtr;
        ((delegate* unmanaged[Stdcall]<IntPtr,IntPtr,void>)vt2[74])(_d2dCtxPtr, bmp1Ptr);
        ((delegate* unmanaged[Stdcall]<IntPtr,void>)vt2[48])(_d2dCtxPtr);
        var clr = new D2D1_COLOR_F { r = 0, g = 0, b = 0, a = 0 };
        ((delegate* unmanaged[Stdcall]<IntPtr,D2D1_COLOR_F*,void>)vt2[47])(_d2dCtxPtr, &clr);

        var bp2 = new D2D1_BITMAP_PROPERTIES
        {
            pixelFormat = new D2D1_PIXEL_FORMAT { format = 87, alphaMode = 1 }, dpiX = 96f, dpiY = 96f,
        };
        IntPtr bmpPtr;
        fixed (byte* pBgra = bgra)
        {
            hr = ((delegate* unmanaged[Stdcall]<IntPtr,D2D1_SIZE_U,void*,uint,D2D1_BITMAP_PROPERTIES*,IntPtr*,int>)vt2[4])(
                _d2dCtxPtr, new D2D1_SIZE_U { width = (uint)w, height = (uint)h },
                pBgra, (uint)(w * 4), &bp2, &bmpPtr);
        }
        if (hr == 0 && bmpPtr != IntPtr.Zero)
        {
            ((delegate* unmanaged[Stdcall]<IntPtr,IntPtr,void*,float,int,void*,void>)vt2[26])(_d2dCtxPtr, bmpPtr, null, 1f, 0, null);
            Marshal.Release(bmpPtr);
        }
        ((delegate* unmanaged[Stdcall]<IntPtr,ulong*,ulong*,int>)vt2[49])(_d2dCtxPtr, null, null);
        ((delegate* unmanaged[Stdcall]<IntPtr,IntPtr,void>)vt2[74])(_d2dCtxPtr, IntPtr.Zero);
        Marshal.Release(bmp1Ptr);

        var vt3 = *(IntPtr**)scPtr;
        ((delegate* unmanaged[Stdcall]<IntPtr,uint,uint,int>)vt3[8])(scPtr, 0, 0);
    }

    // ── Visual states ─────────────────────────────────────────────────────────

    void UpdateVolFill(float vol) { _lastVol = vol; Render(); }

    // ── Hit testing ───────────────────────────────────────────────────────────

    int HitTest(int px, int py)
    {
        if (py < 0 || py >= _physH) return -1;
        for (int i = 0; i < 4; i++)
        {
            int cx = (int)(BtnX[i] * _scale), cw = (int)(LogicalHeight * _scale);
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
        _hovered = hit;
        Render();
    }

    void OnMouseDown(int px, int py)
    {
        int hit = HitTest(px, py);
        _pressed = hit;
        if (hit is >= 0 and <= 3) Render();

        if (hit == 4 && _volSvc != null) { _dragging = true; SeekVolume(px); }
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
        if (old is >= 0 and <= 3) Render();
    }

    void OnMouseLeave()
    {
        _mouseTracking = false;
        _hovered       = -1;
        Render();
    }

    void SeekVolume(int px)
    {
        if (_volSvc == null) return;
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
                _muted   = _volSvc?.GetMute()   ?? _muted;
                _lastVol = _volSvc?.GetVolume() ?? _lastVol;
                Render();
                break;
        }
    }

    // ── Cross-thread sync ─────────────────────────────────────────────────────

    void SyncVolume()
    {
        if (_volSvc == null) return;
        try { _muted = _volSvc.GetMute(); _lastVol = _volSvc.GetVolume(); Render(); }
        catch { }
    }

    void SyncPlayState()
    {
        bool playing = _wasapi?.IsPlaying ?? false;
        if (playing == _isPlaying) return;
        _isPlaying = playing;
        Render();
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

        if (_target     != IntPtr.Zero) { TargetSetRoot(_target, IntPtr.Zero); Marshal.Release(_target); }
        if (_rootSprVis != IntPtr.Zero) Marshal.Release(_rootSprVis);
        foreach (var sc in _swapChains) Marshal.Release(sc);
        if (_d2dCtxPtr  != IntPtr.Zero) Marshal.Release(_d2dCtxPtr);
        if (_compositor    != IntPtr.Zero) Marshal.Release(_compositor);
        if (_compositorRaw != IntPtr.Zero) Marshal.Release(_compositorRaw);
        if (_hwnd != IntPtr.Zero) DestroyWindow(_hwnd);
    }
}
