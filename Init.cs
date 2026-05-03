using System.Runtime.CompilerServices;

namespace WinToolbarMediaButtons;

static class Startup
{
    [ModuleInitializer]
    internal static void Init()
    {
        Environment.SetEnvironmentVariable(
            "MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY",
            AppContext.BaseDirectory);
    }
}
