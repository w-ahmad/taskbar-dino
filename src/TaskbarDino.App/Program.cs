using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using TaskbarDino.Game;
using TaskbarDino.Rendering;
using TaskbarDino.Windows;

namespace TaskbarDino.App;

/// <summary>
/// Application entry point and composition root. The dino runs on a single STA thread that owns the
/// Win32 message loop; a plain <see cref="ServiceProvider"/> supplies DI and logging while the
/// <see cref="CompanionRuntime"/> drives the frame loop on this thread.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        using ServiceProvider services = ConfigureServices();

        ILogger logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("TaskbarDino");
        logger.LogInformation("Taskbar Dino {Version} starting.", typeof(Program).Assembly.GetName().Version);

        try
        {
            services.GetRequiredService<CompanionRuntime>().Run();
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Taskbar Dino terminated with a fatal error.");
            return 1;
        }

        logger.LogInformation("Taskbar Dino stopped.");
        return 0;
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss.fff ";
            });
            builder.AddDebug();
        });

        services.AddWindowsIntegration();
        services.AddRendering();
        services.AddCompanionBrain();
        services.AddApp();

        return services.BuildServiceProvider();
    }
}
