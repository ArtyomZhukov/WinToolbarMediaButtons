using System.Runtime.InteropServices;

namespace WinToolbarMediaButtons.Services;

sealed class WasapiMonitorService : IDisposable
{
    // ── COM interfaces ─────────────────────────────────────────────────────

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, uint stateMask, out IntPtr ppDevices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppEndpoint);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    }

    // Пиковый уровень звука на аудиовыходе — ненулевой когда хоть что-то играет
    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float pfPeak);
    }

    // ── Service ────────────────────────────────────────────────────────────

    readonly IAudioMeterInformation     _meter;
    readonly System.Threading.Timer     _timer;

    public event Action? StateChanged;
    public bool IsPlaying { get; private set; }

    public WasapiMonitorService()
    {
        var clsid  = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
        var enmIid = typeof(IMMDeviceEnumerator).GUID;
        CoCreateInstance(ref clsid, IntPtr.Zero, 1, ref enmIid, out var enmObj);
        var enumerator = (IMMDeviceEnumerator)enmObj;

        enumerator.GetDefaultAudioEndpoint(0 /*eRender*/, 0 /*eConsole*/, out var device);

        var meterIid = typeof(IAudioMeterInformation).GUID;
        device.Activate(ref meterIid, 0, IntPtr.Zero, out var meterObj);
        _meter = (IAudioMeterInformation)meterObj;

        _timer = new System.Threading.Timer(_ => Poll(), null, 0, 200);
    }

    void Poll()
    {
        _meter.GetPeakValue(out var peak);
        var playing = peak > 0.001f;
        if (IsPlaying == playing) return;
        IsPlaying = playing;
        StateChanged?.Invoke();
    }

    public void Dispose() => _timer.Dispose();

    [DllImport("ole32.dll")]
    static extern int CoCreateInstance(ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext,
        ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
}
