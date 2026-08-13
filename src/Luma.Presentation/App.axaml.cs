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

            var options = new InterfaceOptionsService(
                provider.GetRequiredService<ISettingsStore<InterfaceOptions>>());

            // Before the window exists, so the first frame is already in the right
            // language rather than flashing the system one and correcting itself, and so
            // the view-model can be built from settings that are already loaded.
            // Pushed onto the thread pool for the usual reason: this method runs on the
            // UI thread and must not block on a continuation that wants to resume here.
            Task.Run(() => options.RestoreAsync()).GetAwaiter().GetResult();

            var window = new MainWindow();
            var picker = new StorageFilePicker(window);
            var launcher = new ProcessInstallerLauncher(desktop);
            var viewModel = new MainViewModel(player, picker, updates, launcher, options);
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
                //
                // Each step is on its own: a full disk or a locked file is not a reason to
                // show a crash on the way out, and it must not stop the later steps from
                // running. Position in the film goes first, because that is the one worth
                // saving — window geometry is a convenience.
                Task.Run(async () =>
                {
                    await SaveQuietlyAsync(() => preferences.DisposeAsync().AsTask());
                    await SaveQuietlyAsync(() => placementStore.SaveAsync(placement));
                }).GetAwaiter().GetResult();

                // Before the player goes away: the view-model is subscribed to it, and to
                // the localizer singleton that outlives everything here.
                viewModel.Dispose();

                if (player is IAsyncDisposable disposable)
                    disposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Run one shutdown write, swallowing anything it throws.
    ///
    /// Reads are already tolerant — a corrupt settings file yields defaults rather than
    /// refusing to start — but writes were not, and the only place that called them
    /// unguarded was the one place with nowhere to report to. An exception escaping
    /// <c>ShutdownRequested</c> takes the process down with a crash dialog, and takes the
    /// remaining shutdown work with it.
    /// </summary>
    private static async Task SaveQuietlyAsync(Func<Task> write)
    {
        try
        {
            await write().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Nothing sensible to do at this point: the window is already going away, so
            // there is nobody left to tell. Losing the last position or the window size
            // is a far smaller cost than crashing on the way out.
        }
    }
}
