using Microsoft.Extensions.DependencyInjection;

using TaskbarDino.Core.Hosting;
using TaskbarDino.Core.Platform;
using TaskbarDino.Core.Time;
using TaskbarDino.Windows.Foreground;
using TaskbarDino.Windows.Hosting;
using TaskbarDino.Windows.Input;
using TaskbarDino.Windows.Monitors;
using TaskbarDino.Windows.Overlay;
using TaskbarDino.Windows.Startup;
using TaskbarDino.Windows.Taskbar;
using TaskbarDino.Windows.Tray;

namespace TaskbarDino.Windows;

/// <summary>Registers the Win32 integration layer with the DI container.</summary>
public static class WindowsServiceCollectionExtensions
{
    public static IServiceCollection AddWindowsIntegration(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();

        // The message loop is also the frame scheduler; register one instance behind both interfaces.
        services.AddSingleton<Win32MessageLoop>();
        services.AddSingleton<IMessageLoop>(sp => sp.GetRequiredService<Win32MessageLoop>());
        services.AddSingleton<IFrameScheduler>(sp => sp.GetRequiredService<Win32MessageLoop>());

        services.AddSingleton<IOverlayWindow, Win32OverlayWindow>();
        services.AddSingleton<IInputSource, LowLevelMouseInput>();
        services.AddSingleton<IGameInput, LowLevelKeyboardInput>();
        services.AddSingleton<IMonitorService, MonitorService>();
        services.AddSingleton<ITaskbarTracker, WorkAreaTaskbarTracker>();
        services.AddSingleton<IActiveWindowTracker, WinEventForegroundTracker>();

        // Home Position: precise (UI Automation) with best-effort fallback, behind IHomePositionProvider.
        services.AddSingleton<UiaTaskbarButtonLocator>();
        services.AddSingleton<ActiveWindowHomePositionProvider>();
        services.AddSingleton<IHomePositionProvider, CompositeHomePositionProvider>();
        services.AddSingleton<ITrayIcon, ShellTrayIcon>();
        services.AddSingleton<IStartupService, RegistryStartupService>();
        services.AddSingleton<IAccentColorProvider, TaskbarDino.Windows.Theming.WindowsAccentColorProvider>();

        return services;
    }
}
