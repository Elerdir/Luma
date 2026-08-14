using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Luma.Application;
using Luma.Application.Abstractions;
using Luma.Application.Preferences;
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

            // The menu bar along the top of the screen on macOS; nothing anywhere else.
            ApplicationMenu.AttachTo(this, viewModel);

            desktop.MainWindow = window;

            // Everything that asks Luma to open a file goes through here, whenever it
            // asks — see FileOpenQueue.
            var files = new FileOpenQueue(viewModel.OpenPathsAsync);

            // Windows and Linux hand a double-clicked file over as an argument.
            _ = files.OfferAsync(ExistingFiles(desktop.Args));

            // macOS does not: Finder sends an Apple Event, which Avalonia surfaces as
            // an activation. Without this the file associations in Info.plist are
            // decorative — Luma appears under "Open With", is chosen, and opens empty.
            // The same event delivers files opened into an already-running Luma.
            ListenForFileActivation(files);

            // The LibVLC player must be attached to the VideoView only once the window is
            // shown and the native video surface exists — otherwise VideoView.Attach() is a
            // no-op and VLC spawns its own separate output window. After attaching, restore
            // the saved geometry and preferences, then open whatever was asked for.
            window.Opened += async (_, _) =>
            {
                window.AttachEngine(engine);

                var placement = await placementStore.LoadAsync();
                window.ApplyPlacement(placement);
                viewModel.IsPlaylistVisible = placement.IsPlaylistVisible;

                await preferences.RestoreAsync();

                // Only now: volume, repeat mode and the resume point have to be in place
                // before playback starts, or the film opens at the wrong volume and from
                // the beginning.
                await files.ReleaseAsync();

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
    /// Paths from the command line that name a file that is actually there.
    ///
    /// A path that does not exist is dropped rather than reported: it is as likely to
    /// be a switch Luma does not understand as a mistyped file name, and neither is
    /// worth an error dialog in front of someone who wanted to watch something.
    /// </summary>
    private static string[] ExistingFiles(string[]? args) =>
        args is null ? [] : [.. args.Where(File.Exists)];

    /// <summary>
    /// Subscribe to file activation, where the platform has any.
    ///
    /// Only macOS raises it today. The feature is asked for rather than assumed, so
    /// this is a no-op on Windows and Linux instead of a platform check that would
    /// need revisiting the moment another backend grows the same event.
    /// </summary>
    private void ListenForFileActivation(FileOpenQueue files)
    {
        if (this.TryGetFeature<IActivatableLifetime>() is not { } activatable)
            return;

        activatable.Activated += (_, e) =>
        {
            if (e is not FileActivatedEventArgs opened)
                return;

            // A storage item that is not a file on this machine — an iCloud placeholder,
            // something inside a compressed archive — has no path libvlc could open.
            var paths = opened.Files
                .Select(file => file.TryGetLocalPath())
                .Where(path => !string.IsNullOrEmpty(path))
                .Select(path => path!)
                .ToArray();

            // Nothing awaits this: it is an event handler, and the work it starts
            // reports its own failures through the status line.
            _ = files.OfferAsync(paths);
        };
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
