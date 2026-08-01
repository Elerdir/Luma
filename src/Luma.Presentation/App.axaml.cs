using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Luma.Application;
using Luma.Application.Abstractions;
using Luma.Application.Preferences;
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
            var preferences = provider.GetRequiredService<PreferenceTracker>();
            var placementStore = provider.GetRequiredService<ISettingsStore<WindowPlacement>>();
            var updates = provider.GetRequiredService<IUpdateService>();

            var language = new LanguageService(
                provider.GetRequiredService<ISettingsStore<InterfaceOptions>>());

            // Before the window exists, so the first frame is already in the right
            // language rather than flashing the system one and correcting itself.
            // Pushed onto the thread pool for the usual reason: this method runs on the
            // UI thread and must not block on a continuation that wants to resume here.
            Task.Run(() => language.RestoreAsync()).GetAwaiter().GetResult();

            var window = new MainWindow();
            var picker = new StorageFilePicker(window);
            var launcher = new ProcessInstallerLauncher(desktop);
            var viewModel = new MainViewModel(player, picker, updates, launcher, language);
            window.DataContext = viewModel;

            desktop.MainWindow = window;

            // The LibVLC player must be attached to the VideoView only once the window is
            // shown and the native video surface exists — otherwise VideoView.Attach() is a
            // no-op and VLC spawns its own separate output window. After attaching, restore
            // the saved geometry and preferences, then honor any file passed on the command
            // line (file association / "luma movie.mkv").
            var startupFile = desktop.Args is { Length: > 0 } args && File.Exists(args[0]) ? args[0] : null;
            window.Opened += async (_, _) =>
            {
                window.AttachEngine(engine);

                var placement = await placementStore.LoadAsync();
                window.ApplyPlacement(placement);
                viewModel.IsPlaylistVisible = placement.IsPlaylistVisible;

                await preferences.RestoreAsync();

                if (startupFile is not null)
                    await player.OpenAsync(MediaSource.FromFile(startupFile));

                // Deliberately last and deliberately not awaited into the startup path:
                // an update check must never delay the window becoming usable, and it
                // stays silent when no server is configured.
                _ = viewModel.CheckForUpdatesAsync();
            };

            // PlayerService, the engine and the preference tracker are all IAsyncDisposable;
            // dispose them explicitly on exit (the DI container's synchronous Dispose cannot
            // dispose async-only services). Preferences are flushed before playback is torn
            // down, so the final position is still readable.
            desktop.ShutdownRequested += (_, _) =>
            {
                var placement = window.CapturePlacement(viewModel.IsPlaylistVisible);

                // Shutdown has to block until the writes finish, and this runs on the UI
                // thread — so the file work is pushed onto the thread pool first. Awaiting
                // it directly here would deadlock the moment a continuation asked to
                // resume on the dispatcher we are busy blocking.
                Task.Run(async () =>
                {
                    await placementStore.SaveAsync(placement);
                    await preferences.DisposeAsync();
                }).GetAwaiter().GetResult();

                if (player is IAsyncDisposable disposable)
                    disposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
