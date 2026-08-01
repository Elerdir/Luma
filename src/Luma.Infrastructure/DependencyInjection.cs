using Luma.Application;
using Luma.Application.Abstractions;
using Luma.Application.Preferences;
using Luma.Infrastructure.Media;
using Luma.Infrastructure.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace Luma.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the LibVLC-backed media engine, the application player and the
    /// JSON-backed settings adapter. The concrete <see cref="LibVlcMediaEngine"/> is
    /// also resolvable so the composition root can hand its <c>MediaPlayer</c> to the
    /// video surface.
    /// </summary>
    public static IServiceCollection AddLumaInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<LibVlcMediaEngine>();
        services.AddSingleton<IMediaEngine>(sp => sp.GetRequiredService<LibVlcMediaEngine>());
        services.AddSingleton<IPlayer, PlayerService>();

        // Open generic: each settings record gets its own JSON file, named after the type.
        services.AddSingleton(typeof(ISettingsStore<>), typeof(JsonSettingsStore<>));
        services.AddSingleton<PreferenceTracker>();

        return services;
    }
}
