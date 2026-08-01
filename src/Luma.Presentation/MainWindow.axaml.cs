using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LibVLCSharp.Avalonia;
using Luma.Infrastructure.Media;
using Luma.Presentation.Controls;
using Luma.Presentation.Localization;
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
        // Bubble, so a control that scrolls on its own — a combo box, a slider — gets
        // the wheel first and this only sees what nothing else wanted.
        AddHandler(PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Bubble);
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
            overlay.AddHandler(PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Bubble);
            overlay.AddHandler(PointerReleasedEvent, OnVideoPointerReleased, RoutingStrategies.Bubble);
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
            vm.StatusText = Localizer.Instance.Format("Status.Error", ex.Message);
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

    /// <summary>
    /// Whether an event came from inside the floating controls. They sit inside the
    /// video overlay, so their clicks bubble to the handlers watching the video —
    /// without this, double-clicking Play would also toggle fullscreen and
    /// right-clicking the seek bar would open the video's menu.
    /// </summary>
    private static bool CameFromTransportBar(object? source) =>
        source is Visual visual && visual.FindAncestorOfType<TransportBar>() is not null;

    private void OnVideoAreaDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (CameFromTransportBar(e.Source))
            return;

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

            // Page down/up step through the folder the file came from — the next and
            // previous episode. The same thing the ⏭ and ⏮ buttons do, on the keys a
            // person's hand is already near while watching.
            case Key.PageDown:
                e.Handled = Invoke(vm => vm.NextCommand);
                break;
            case Key.PageUp:
                e.Handled = Invoke(vm => vm.PreviousCommand);
                break;
        }

        base.OnKeyDown(e);
    }

    /// <summary>
    /// Run a view-model command if it is currently allowed, reporting whether it ran.
    /// A key that could not do anything is left unhandled rather than silently eaten.
    /// </summary>
    private bool Invoke(Func<MainViewModel, ICommand> pick)
    {
        if (DataContext is not MainViewModel vm)
            return false;

        var command = pick(vm);
        if (!command.CanExecute(null))
            return false;

        command.Execute(null);
        return true;
    }

    /// <summary>
    /// Opens the video's context menu on a right-click.
    ///
    /// The flyout is declared on the overlay but opened by hand and anchored to
    /// VideoArea in the main window. Left to itself a ContextFlyout inside VideoView's
    /// floating window never opened at all — right-clicking the video produced no menu
    /// and no popup window. Anchoring it to the main window sidesteps that.
    /// </summary>
    private void OnVideoPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton is not MouseButton.Right || CameFromTransportBar(e.Source))
            return;

        var overlay = this.FindControl<Panel>("VideoOverlay");
        var anchor = this.FindControl<Panel>("VideoArea");

        // PopupFlyoutBase rather than FlyoutBase: only the former can be placed at the
        // pointer, which is what a context menu has to do.
        if (overlay?.ContextFlyout is PopupFlyoutBase flyout && anchor is not null)
        {
            flyout.ShowAt(anchor, showAtPointer: true);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Fullscreen is window state rather than player state, so it lives here rather
    /// than on the view-model and the menu reaches it through a click handler.
    /// </summary>
    private void OnFullscreenMenuClick(object? sender, RoutedEventArgs e) => ToggleFullscreen();

    private void ToggleFullscreen() => SetFullscreen(!_isFullscreen);

    private void SetFullscreen(bool on)
    {
        if (on == _isFullscreen)
            return;

        _isFullscreen = on;

        var docked = this.FindControl<TransportBar>("DockedTransport");

        if (on)
        {
            _stateBeforeFullscreen = WindowState;
            WindowState = WindowState.FullScreen;

            // The docked copy goes away entirely so the video gets the whole screen;
            // the floating one takes over and auto-hides.
            if (docked is not null) docked.IsVisible = false;
            RevealControls();
        }
        else
        {
            _idleTimer.Stop();
            SetControlsVisible(false);
            if (docked is not null) docked.IsVisible = true;
            ShowCursor();
            WindowState = _stateBeforeFullscreen;
        }
    }

    // ---- Fullscreen chrome: float the controls over the video and hide them while idle ----
    //
    // Two instances of one TransportBar: the docked one is hidden for the duration of
    // fullscreen so the video has the whole screen, and the instance inside the video
    // overlay floats over the picture instead. An earlier attempt re-parented a single
    // instance between the two places and silently did nothing, because the overlay
    // lives in VideoView's own top-level window and controls do not move between those.

    private static readonly TimeSpan IdleBeforeHiding = TimeSpan.FromSeconds(3);
    private static readonly Cursor HiddenCursor = new(StandardCursorType.None);
    private static readonly Cursor VisibleCursor = Cursor.Default;

    private readonly DispatcherTimer _idleTimer = new() { Interval = IdleBeforeHiding };

    private void SetControlsVisible(bool visible)
    {
        if (this.FindControl<TransportBar>("FullscreenTransport") is { } floating)
            floating.IsVisible = visible;
    }

    /// <summary>
    /// Wheel anywhere over the window changes the volume, the way every other media
    /// player behaves. Reaching here at all means no control underneath the pointer
    /// claimed the wheel for its own scrolling.
    /// </summary>
    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not MainViewModel vm || e.Delta.Y == 0)
            return;

        if (e.Delta.Y > 0)
            vm.VolumeUpCommand.Execute(null);
        else
            vm.VolumeDownCommand.Execute(null);

        e.Handled = true;
    }

    private object? _lastPointerSource;
    private Point _lastPointerPosition;

    private void OnPointerMovedAnywhere(object? sender, PointerEventArgs e)
    {
        if (!_isFullscreen)
            return;

        // Only a real change of position counts as activity.
        //
        // Hiding the bar gives its row back to the video, which reflows the layout
        // underneath a completely stationary pointer — and Avalonia reports that as
        // pointer movement. Treating it as activity re-showed the bar, which reflowed
        // again, and the bar flickered for as long as the pointer stayed over the
        // window. Move the mouse to another screen and it settled, which is what gave
        // the loop away.
        var position = e.GetPosition(sender as Visual);
        if (ReferenceEquals(sender, _lastPointerSource) &&
            Math.Abs(position.X - _lastPointerPosition.X) < 1 &&
            Math.Abs(position.Y - _lastPointerPosition.Y) < 1)
            return;

        _lastPointerSource = sender;
        _lastPointerPosition = position;
        RevealControls();
    }

    /// <summary>Show the chrome and restart the idle countdown.</summary>
    private void RevealControls()
    {
        SetControlsVisible(true);
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
        var transport = this.FindControl<TransportBar>("FullscreenTransport");
        if (transport?.IsPointerOver == true)
        {
            _idleTimer.Start();
            return;
        }

        SetControlsVisible(false);
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
