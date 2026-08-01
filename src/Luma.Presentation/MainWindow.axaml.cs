using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LibVLCSharp.Avalonia;
using Luma.Infrastructure.Media;
using Luma.Presentation.Services;
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
        AddHandler(PointerMovedEvent, OnPointerMovedAnywhere, RoutingStrategies.Tunnel);
        _idleTimer.Tick += OnIdleElapsed;
        Resized += OnResized;

        // The video overlay lives in VideoView's own floating window, so routed events
        // raised there never reach this one — it needs the handlers of its own.
        var overlay = this.FindControl<Panel>("VideoOverlay");
        if (overlay is not null)
        {
            DragDrop.SetAllowDrop(overlay, true);
            overlay.AddHandler(DragDrop.DragOverEvent, OnDragOver);
            overlay.AddHandler(DragDrop.DropEvent, OnDrop);
            overlay.AddHandler(PointerMovedEvent, OnPointerMovedAnywhere, RoutingStrategies.Tunnel);
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

    /// <summary>Restore remembered geometry. Ignores sizes that no longer fit a screen.</summary>
    public void ApplyPlacement(WindowPlacement placement)
    {
        if (placement.Width >= MinWidth && placement.Height >= MinHeight)
        {
            Width = placement.Width;
            Height = placement.Height;
            _lastNormalSize = new Size(placement.Width, placement.Height);
        }

        if (placement.IsMaximized)
            WindowState = WindowState.Maximized;
    }

    /// <summary>
    /// The size the window last had while merely windowed. Maximized and fullscreen
    /// sizes are the screen's rather than the user's choice, so they are not what we
    /// want to restore to.
    /// </summary>
    private Size _lastNormalSize = new(960, 600);

    private void OnResized(object? sender, WindowResizedEventArgs e)
    {
        if (WindowState is WindowState.Normal)
            _lastNormalSize = new Size(Width, Height);
    }

    /// <summary>Snapshot the geometry worth remembering for next launch.</summary>
    public WindowPlacement CapturePlacement(bool isPlaylistVisible) => new()
    {
        Width = _lastNormalSize.Width,
        Height = _lastNormalSize.Height,
        IsMaximized = WindowState is WindowState.Maximized,
        IsPlaylistVisible = isPlaylistVisible
    };

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

        if (on)
        {
            _stateBeforeFullscreen = WindowState;
            WindowState = WindowState.FullScreen;
            MoveTransportToOverlay();
            RevealControls();
        }
        else
        {
            _idleTimer.Stop();
            MoveTransportToDock();
            ShowCursor();
            WindowState = _stateBeforeFullscreen;
        }
    }

    // ---- Fullscreen chrome: float the transport bar over the video and fade it out ----

    private static readonly TimeSpan IdleBeforeHiding = TimeSpan.FromSeconds(3);
    private static readonly Cursor HiddenCursor = new(StandardCursorType.None);
    private static readonly Cursor VisibleCursor = Cursor.Default;

    private readonly DispatcherTimer _idleTimer = new() { Interval = IdleBeforeHiding };

    /// <summary>
    /// Re-parents the docked transport bar into the video overlay. Re-parenting rather
    /// than keeping a second copy in XAML means the controls, bindings and shortcuts
    /// have exactly one definition.
    /// </summary>
    private void MoveTransportToOverlay()
    {
        var transport = this.FindControl<Border>("TransportBar");
        var host = this.FindControl<Panel>("FullscreenTransportHost");
        var root = this.FindControl<Grid>("RootGrid");
        if (transport is null || host is null || root is null)
            return;

        root.Children.Remove(transport);
        host.Children.Add(transport);
        host.IsVisible = true;
    }

    private void MoveTransportToDock()
    {
        var transport = this.FindControl<Border>("TransportBar");
        var host = this.FindControl<Panel>("FullscreenTransportHost");
        var root = this.FindControl<Grid>("RootGrid");
        if (transport is null || host is null || root is null)
            return;

        host.Children.Remove(transport);
        host.IsVisible = false;
        // Grid.Row/Column are attached to the Border itself, so they survive the move.
        root.Children.Add(transport);
        transport.IsVisible = true;
    }

    private void OnPointerMovedAnywhere(object? sender, PointerEventArgs e)
    {
        if (_isFullscreen)
            RevealControls();
    }

    /// <summary>Show the chrome and restart the idle countdown.</summary>
    private void RevealControls()
    {
        var host = this.FindControl<Panel>("FullscreenTransportHost");
        if (host is not null)
            host.IsVisible = true;

        ShowCursor();

        _idleTimer.Stop();
        _idleTimer.Start();
    }

    private void OnIdleElapsed(object? sender, EventArgs e)
    {
        _idleTimer.Stop();

        if (!_isFullscreen)
            return;

        // Keep the bar up while the pointer rests on it, otherwise it vanishes from
        // under a user who is reaching for the seek slider.
        var transport = this.FindControl<Border>("TransportBar");
        if (transport?.IsPointerOver == true)
        {
            _idleTimer.Start();
            return;
        }

        var host = this.FindControl<Panel>("FullscreenTransportHost");
        if (host is not null)
            host.IsVisible = false;

        HideCursor();
    }

    // The overlay is its own top-level, so the cursor has to be set on both surfaces.
    private void ShowCursor() => SetCursorOnAllSurfaces(VisibleCursor);

    private void HideCursor() => SetCursorOnAllSurfaces(HiddenCursor);

    private void SetCursorOnAllSurfaces(Cursor cursor)
    {
        Cursor = cursor;

        var overlay = this.FindControl<Panel>("VideoOverlay");
        if (overlay is not null)
            overlay.Cursor = cursor;
    }
}
