using Luma.Application;
using Luma.Application.Abstractions;
using Luma.Infrastructure.Media;
using Microsoft.Extensions.DependencyInjection;

namespace Luma.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the LibVLC-backed media engine and the application player.
    /// The concrete <see cref="LibVlcMediaEngine"/> is also resolvable so the
    /// composition root can hand its <c>MediaPlayer</c> to the video surface.
    /// </summary>
    public static IServiceCollection AddLumaInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<LibVlcMediaEngine>();
        services.AddSingleton<IMediaEngine>(sp => sp.GetRequiredService<LibVlcMediaEngine>());
        services.AddSingleton<IPlayer, PlayerService>();
        return services;
    }
}
