using System.Windows;

namespace BeautyDock;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        bool openSettings = args.Any(a => a.Equals("--settings", StringComparison.OrdinalIgnoreCase));
        bool toggleDock = args.Any(a => a.Equals(DesktopDockMenu.ToggleArgument, StringComparison.OrdinalIgnoreCase));
        using var mutex = new Mutex(true, "BeautyDock.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            if (toggleDock) DesktopDockMenu.ToggleRunningDock();
            return;
        }

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) =>
        {
            Log(e.Exception.ToString());
            e.Handled = true;
        };

        var dock = new DockWindow(DockConfig.Load(), openSettings);
        DesktopDockMenu.Update(hidden: false);
        dock.Show();
        app.Run();
    }

    public static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(DockConfig.Dir);
            File.AppendAllText(System.IO.Path.Combine(DockConfig.Dir, "error.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch { }
    }
}
