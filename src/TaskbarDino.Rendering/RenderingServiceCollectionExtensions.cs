using Microsoft.Extensions.DependencyInjection;

using TaskbarDino.Core.Graphics;

namespace TaskbarDino.Rendering;

/// <summary>Registers the DirectComposition rendering layer with the DI container.</summary>
public static class RenderingServiceCollectionExtensions
{
    public static IServiceCollection AddRendering(this IServiceCollection services)
    {
        services.AddSingleton<IRendererFactory, DirectCompositionRendererFactory>();
        return services;
    }
}
