using System.Runtime.InteropServices;

namespace WinToolbarMediaButtons.Services;

enum VolumeKey : byte
{
    Mute = 0xAD,
    Down = 0xAE,
    Up   = 0xAF,
}

static class VolumeService
{
    [DllImport("user32.dll")]
    static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, nint dwExtraInfo);

    private const uint KEYEVENTF_KEYUP = 0x0002;

    public static void Send(VolumeKey key)
    {
        keybd_event((byte)key, 0, 0, 0);
        keybd_event((byte)key, 0, KEYEVENTF_KEYUP, 0);
    }
}
