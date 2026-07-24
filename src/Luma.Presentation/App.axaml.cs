using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Luma.Application;
using Luma.Domain.Media;
using Luma.Infrastructure;
using Luma.Infrastructure.Media;
using Luma.Presentation.Services;
using Luma.Presentation.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Luma.Presentation;

public partial class App : Avalonia.Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = new ServiceCollection();
            services.AddLumaInfrastructure();
            var provider = services.BuildServiceProvider();

            var player = provider.GetRequiredService<IPlayer>();
            var engine = provider.GetRequiredService<LibVlcMediaEngine>();

            var window = new MainWindow();
            var picker = new StorageFilePicker(window);
            window.DataContext = new MainViewModel(player, picker);

            desktop.MainWindow = window;

            // The LibVLC player must be attached to the VideoView only once the window is
            // shown and the native video surface exists — otherwise VideoView.Attach() is a
            // no-op and VLC spawns its own separate output window. After attaching, honor any
            // file passed on the command line (file association / "luma movie.mkv").
            var startupFile = desktop.Args is { Length: > 0 } args && File.Exists(args[0]) ? args[0] : null;
            window.Opened += async (_, _) =>
            {
                window.AttachEngine(engine);
                if (startupFile is not null)
                    await player.OpenAsync(MediaSource.FromFile(startupFile));
            };

            // PlayerService/engine are IAsyncDisposable; dispose them explicitly on exit
            // (the DI container's synchronous Dispose cannot dispose async-only services).
            desktop.ShutdownRequested += (_, _) =>
            {
                if (player is IAsyncDisposable disposable)
                    disposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
