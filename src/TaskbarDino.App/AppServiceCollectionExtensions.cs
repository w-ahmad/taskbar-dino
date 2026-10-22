using Microsoft.Extensions.DependencyInjection;

using TaskbarDino.Core.Settings;

namespace TaskbarDino.App;

/// <summary>Registers the application-level services (composition-root layer).</summary>
public static class AppServiceCollectionExtensions
{
    public static IServiceCollection AddApp(this IServiceCollection services)
    {
        services.AddSingleton<ISettingsService, JsonSettingsService>();
        services.AddSingleton<GameSession>();
        services.AddSingleton<CompanionRuntime>();
        return services;
    }
}
