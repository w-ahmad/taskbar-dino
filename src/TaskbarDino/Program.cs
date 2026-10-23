using TaskbarDino.Win32;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;

namespace TaskbarDino;

/// <summary>
/// Entry point and composition root. Everything runs on this one STA thread, which owns the Win32
/// message loop; there is no DI container because a dozen objects wired by hand is simpler and
/// starts faster. A second launch just tells the running instance to show itself.
/// </summary>
internal static unsafe class Program
{
    private const string MutexName = @"Local\TaskbarDino.SingleInstance";

    [STAThread]
    private static int Main(string[] args)
    {
        if (Array.IndexOf(args, "--log") >= 0 || Environment.GetEnvironmentVariable("TASKBARDINO_LOG") == "1")
        {
            Log.EnableFile();
        }

        using var mutex = new Mutex(initiallyOwned: true, MutexName, out bool first);
        if (!first)
        {
            fixed (char* cls = OverlayWindow.ClassName)
            {
                HWND existing = PInvoke.FindWindow(cls, (PCWSTR)null);
                if (!existing.IsNull)
                {
                    _ = PInvoke.PostMessage(existing, OverlayWindow.ShowRequestMessage, default, default);
                }
            }

            return 0;
        }

        _ = PInvoke.CoInitializeEx(null, COINIT.COINIT_APARTMENTTHREADED | COINIT.COINIT_DISABLE_OLE1DDE);
        Log.Info($"Taskbar Dino {typeof(Program).Assembly.GetName().Version} starting.");

        try
        {
            using var overlay = new OverlayWindow();
            using var mouse = new MouseHook();
            using var keyboard = new KeyboardHook();
            using var foreground = new ForegroundTracker();
            using var tray = new TrayIcon();
            using var locator = new TaskbarButtonLocator();
            var settings = new SettingsStore();
            using var loop = new MessageLoop();
            using var runtime = new CompanionRuntime(overlay, mouse, keyboard, foreground, tray, settings, loop, new HomeResolver(locator))
            {
                StartInGame = Array.IndexOf(args, "--game") >= 0,
                Autopilot = Array.IndexOf(args, "--autopilot") >= 0,
            };
            runtime.Run();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "fatal");
            return 1;
        }

        Log.Info($"Taskbar Dino stopped.");
        return 0;
    }
}
