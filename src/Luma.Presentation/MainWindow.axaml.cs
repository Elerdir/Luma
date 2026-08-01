using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using LibVLCSharp.Avalonia;
using Luma.Infrastructure.Media;
using Luma.Presentation.ViewModels;

namespace Luma.Presentation;

public partial class MainWindow : Window
{
    private WindowState _stateBeforeFullscreen = WindowState.Normal;
    private bool _isFullscreen;

    public MainWindow()
    {
        InitializeComponent();

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        // The video overlay lives in VideoView's own floating window, so routed events
        // raised there never reach this one — it needs the handlers of its own.
        var overlay = this.FindControl<Panel>("VideoOverlay");
        if (overlay is not null)
        {
            DragDrop.SetAllowDrop(overlay, true);
            overlay.AddHandler(DragDrop.DragOverEvent, OnDragOver);
            overlay.AddHandler(DragDrop.DropEvent, OnDrop);
        }
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        // Holding Shift appends to the playlist instead of replacing it — the same
        // modifier convention as the file managers dragging the files in.
        var hasFiles = e.DataTransfer.Contains(DataFormat.File);
        e.DragEffects = hasFiles
            ? e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? DragDropEffects.Link : DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;

        if (DataContext is not MainViewModel vm)
            return;

        // An async void handler has nowhere to propagate, so nothing may escape it.
        try
        {
            var paths = e.DataTransfer.TryGetFiles()?
                .OfType<IStorageFile>()
                .Select(f => f.TryGetLocalPath())
                .Where(p => !string.IsNullOrEmpty(p))
                .Select(p => p!)
                .ToArray() ?? [];

            if (paths.Length == 0)
                return;

            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                await vm.EnqueuePathsAsync(paths);
            else
                await vm.OpenPathsAsync(paths);
        }
        catch (Exception ex)
        {
            vm.StatusText = $"Error: {ex.Message}";
        }
    }

    /// <summary>Wire the concrete LibVLC player into the video surface (composition-root concern).</summary>
    public void AttachEngine(LibVlcMediaEngine engine)
    {
        var videoView = this.FindControl<VideoView>("VideoView");
        if (videoView is not null)
            videoView.MediaPlayer = engine.Player;
    }

    private DateTime _lastFullscreenToggle = DateTime.MinValue;

    private void OnVideoAreaDoubleTapped(object? sender, TappedEventArgs e)
    {
        // VideoView hosts the overlay in a separate floating window and forwards its
        // pointer input onward, so one physical double-click surfaces twice. Without
        // this guard the two deliveries toggle fullscreen on and straight back off.
        var now = DateTime.UtcNow;
        if (now - _lastFullscreenToggle < TimeSpan.FromMilliseconds(300))
            return;
        _lastFullscreenToggle = now;

        ToggleFullscreen();
        e.Handled = true;
    }

    /// <summary>
    /// VideoView hosts its content in a separate floating window, so the data context
    /// does not inherit down the visual tree — propagate it explicitly.
    /// </summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        var overlay = this.FindControl<Panel>("VideoOverlay");
        if (overlay is not null)
            overlay.DataContext = DataContext;
    }

    /// <summary>Double-clicking a playlist row plays it, the usual convention.</summary>
    private void OnPlaylistDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.PlaySelectedCommand.CanExecute(null))
            vm.PlaySelectedCommand.Execute(null);

        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.F or Key.F11:
                ToggleFullscreen();
                e.Handled = true;
                break;
            case Key.Escape when _isFullscreen:
                SetFullscreen(false);
                e.Handled = true;
                break;
        }

        base.OnKeyDown(e);
    }

    private void ToggleFullscreen() => SetFullscreen(!_isFullscreen);

    private void SetFullscreen(bool on)
    {
        if (on == _isFullscreen)
            return;

        _isFullscreen = on;
        var transport = this.FindControl<Border>("TransportBar");

        if (on)
        {
            _stateBeforeFullscreen = WindowState;
            WindowState = WindowState.FullScreen;
            if (transport is not null) transport.IsVisible = false;
        }
        else
        {
            WindowState = _stateBeforeFullscreen;
            if (transport is not null) transport.IsVisible = true;
        }
    }
}
