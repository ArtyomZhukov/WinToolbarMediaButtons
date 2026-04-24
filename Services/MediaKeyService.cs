using System.Runtime.InteropServices;

namespace WinToolbarMediaButtons.Services;

enum MediaKey : byte
{
    NextTrack = 0xB0,
    PrevTrack = 0xB1,
    PlayPause = 0xB3,
}

static class MediaKeyService
{
    [DllImport("user32.dll")]
    static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, nint dwExtraInfo);

    private const uint KEYEVENTF_KEYUP = 0x0002;

    public static void Send(MediaKey key)
    {
        keybd_event((byte)key, 0, 0, 0);
        keybd_event((byte)key, 0, KEYEVENTF_KEYUP, 0);
    }
}
