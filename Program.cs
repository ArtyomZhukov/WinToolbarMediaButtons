namespace WinToolbarMediaButtons;

static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.ThreadException += (_, e) =>
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "WinToolbarCrash.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}]\n" +
                    $"{e.Exception?.GetType()?.FullName}: {e.Exception?.Message}\n" +
                    $"{e.Exception?.StackTrace}\n\n");
            }
            catch { }
        };
        Application.Run(new MainForm());
    }
}
