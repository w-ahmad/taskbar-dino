using Microsoft.Extensions.DependencyInjection;

using TaskbarDino.Core.Dino;

namespace TaskbarDino.Game;

/// <summary>Registers the companion behaviour / game-logic layer with the DI container.</summary>
public static class GameServiceCollectionExtensions
{
    public static IServiceCollection AddCompanionBrain(this IServiceCollection services)
    {
        services.AddSingleton<IDinoBrain, DinoBrain>();
        return services;
    }
}
