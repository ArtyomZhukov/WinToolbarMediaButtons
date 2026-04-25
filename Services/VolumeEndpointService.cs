using System.Runtime.InteropServices;

namespace WinToolbarMediaButtons.Services;

sealed class VolumeEndpointService : IDisposable
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

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr pNotify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr pNotify);
        [PreserveSig] int GetChannelCount(out uint pnChannelCount);
        [PreserveSig] int SetMasterVolumeLevel(float fLevelDB, IntPtr pguidEventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float fLevel, IntPtr pguidEventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float pfLevelDB);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float pfLevel);
        [PreserveSig] int SetChannelVolumeLevel(uint nChannel, float fLevelDB, IntPtr pguidEventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, IntPtr pguidEventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, IntPtr pguidEventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
        [PreserveSig] int GetVolumeStepInfo(out uint pnStep, out uint pnStepCount);
        [PreserveSig] int VolumeStepUp(IntPtr pguidEventContext);
        [PreserveSig] int VolumeStepDown(IntPtr pguidEventContext);
        [PreserveSig] int QueryHardwareSupport(out uint pdwHardwareSupportMask);
        [PreserveSig] int GetVolumeRange(out float pflMin, out float pflMax, out float pflIncrement);
    }

    // ── Service ────────────────────────────────────────────────────────────

    readonly IAudioEndpointVolume _endpoint;

    public VolumeEndpointService()
    {
        var clsid  = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
        var enmIid = typeof(IMMDeviceEnumerator).GUID;
        CoCreateInstance(ref clsid, IntPtr.Zero, 1, ref enmIid, out var enmObj);
        var enumerator = (IMMDeviceEnumerator)enmObj;

        enumerator.GetDefaultAudioEndpoint(0 /*eRender*/, 0 /*eConsole*/, out var device);

        var epIid = typeof(IAudioEndpointVolume).GUID;
        device.Activate(ref epIid, 0, IntPtr.Zero, out var epObj);
        _endpoint = (IAudioEndpointVolume)epObj;
    }

    public float GetVolume()
    {
        _endpoint.GetMasterVolumeLevelScalar(out var v);
        return v;
    }

    public void SetVolume(float level)
        => _endpoint.SetMasterVolumeLevelScalar(Math.Clamp(level, 0f, 1f), IntPtr.Zero);

    public bool GetMute()
    {
        _endpoint.GetMute(out var m);
        return m;
    }

    public void ToggleMute() => _endpoint.SetMute(!GetMute(), IntPtr.Zero);

    public void Dispose() { }

    [DllImport("ole32.dll")]
    static extern int CoCreateInstance(ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext,
        ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
}
