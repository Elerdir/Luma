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
        ListenForKeysOn(this);
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
            // Key events route from whatever holds focus up to its root. Click the
            // picture and focus lands on the root of VideoView's floating window, above
            // this panel — so the handlers have to sit on that window, not here. It does
            // not exist until the overlay is attached.
            overlay.AttachedToVisualTree += (_, _) => ListenForKeysOn(TopLevel.GetTopLevel(overlay));
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
    /// Whether an event came from inside the chrome floating over the picture — the
    /// transport bar or the playlist. Both sit inside the video overlay, so their clicks
    /// bubble to the handlers watching the video: without this, double-clicking Play
    /// would also toggle fullscreen and right-clicking a playlist row would open the
    /// video's menu.
    /// </summary>
    private static bool CameFromChrome(object? source) =>
        source is Visual visual &&
        (visual.FindAncestorOfType<TransportBar>() is not null ||
         visual.FindAncestorOfType<PlaylistPanel>() is not null);

    private void OnVideoAreaDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (CameFromChrome(e.Source))
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

    // ---- Keyboard shortcuts ----
    //
    // These were Window.KeyBindings, and mostly did not work. A KeyBinding only fires
    // once the key event reaches the window, and two everyday situations stop it:
    //
    //   * Click any control in the transport bar and it keeps focus. Space is what
    //     presses a focused button, so the button swallowed it and playback never
    //     paused — the symptom that started this.
    //   * The video overlay is VideoView's own top-level window. Keys pressed while the
    //     picture has focus are delivered there and never reach this window.
    //
    // So the handler is registered on both surfaces, and the keys a focused control
    // would otherwise swallow are claimed on the way down. Everything else waits for
    // the bubble pass, so a combo box, a slider or the playlist keeps its own arrows.

    /// <summary>
    /// Keys that a focused control would consume before the window ever saw them.
    /// Deliberately the shortest possible list: taking a key on the way down means
    /// taking it away from whatever is focused.
    /// </summary>
    private static bool IsClaimedOnTheWayDown(Key key) => key is Key.Space;

    private readonly HashSet<TopLevel> _keyboardSurfaces = [];

    /// <summary>
    /// Give a top-level the same shortcuts this window has. Idempotent: the overlay is
    /// re-attached whenever the video surface is rebuilt.
    /// </summary>
    private void ListenForKeysOn(TopLevel? surface)
    {
        if (surface is null || !_keyboardSurfaces.Add(surface))
            return;

        surface.AddHandler(KeyDownEvent, OnKeyDownTunnel, RoutingStrategies.Tunnel);
        surface.AddHandler(KeyDownEvent, OnKeyDownBubble, RoutingStrategies.Bubble);
        surface.AddHandler(KeyUpEvent, OnKeyUpTunnel, RoutingStrategies.Tunnel);
    }

    private void OnKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        if (!IsClaimedOnTheWayDown(e.Key))
            return;

        e.Handled = HandleShortcut(e);
        _swallowNextSpaceUp = e.Handled && e.Key is Key.Space;
    }

    private bool _swallowNextSpaceUp;

    /// <summary>
    /// A button presses on Space down but clicks on Space up, so claiming only the down
    /// half still let a focused button fire: Space paused the video <em>and</em> pressed
    /// whatever had last been clicked. Eat the release that belongs to a Space we took.
    /// </summary>
    private void OnKeyUpTunnel(object? sender, KeyEventArgs e)
    {
        if (e.Key is not Key.Space || !_swallowNextSpaceUp)
            return;

        _swallowNextSpaceUp = false;
        e.Handled = true;
    }

    private void OnKeyDownBubble(object? sender, KeyEventArgs e)
    {
        if (e.Handled || IsClaimedOnTheWayDown(e.Key))
            return;

        e.Handled = HandleShortcut(e);
    }

    /// <summary>
    /// Run the shortcut for a key, reporting whether anything happened. A key that
    /// could not do anything — Next with nothing to go to — is left unhandled rather
    /// than silently eaten.
    /// </summary>
    private bool HandleShortcut(KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return false;

        // Never take a key away from somewhere text is being typed.
        if (e.Source is TextBox)
            return false;

        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);

        // Ctrl+O is the only shortcut with a modifier of its own; anything else held
        // down means the user is asking for something that is not ours.
        if (alt || (control && e.Key is not Key.O))
            return false;

        return e.Key switch
        {
            Key.Space or Key.K => Run(vm.PlayPauseCommand),
            Key.Left => Run(shift ? vm.SeekBackwardLargeCommand : vm.SeekBackwardCommand),
            Key.Right => Run(shift ? vm.SeekForwardLargeCommand : vm.SeekForwardCommand),
            Key.Up => Run(vm.VolumeUpCommand),
            Key.Down => Run(vm.VolumeDownCommand),
            Key.M => Run(vm.ToggleMuteCommand),
            Key.S => Run(vm.StopCommand),
            Key.R => Run(vm.CycleRepeatCommand),
            Key.L => Run(vm.TogglePlaylistCommand),
            Key.O when control => Run(vm.OpenCommand),

            // Next and previous, on both the letters and the keys a hand already rests
            // near while watching: page down is the next episode in the folder.
            Key.N or Key.PageDown => Run(vm.NextCommand),
            Key.P or Key.PageUp => Run(vm.PreviousCommand),

            Key.F or Key.F11 => ToggleFullscreenFromKey(),
            Key.Escape when _isFullscreen => ToggleFullscreenFromKey(),
            _ => false
        };
    }

    private static bool Run(ICommand command)
    {
        if (!command.CanExecute(null))
            return false;

        command.Execute(null);
        return true;
    }

    private bool ToggleFullscreenFromKey()
    {
        ToggleFullscreen();
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
        if (e.InitialPressMouseButton is not MouseButton.Right || CameFromChrome(e.Source))
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

        // The playlist swaps between its docked and floating instance on this.
        if (DataContext is MainViewModel vm)
            vm.IsFullscreen = on;

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
        // under a user who is reaching for the seek slider. The same goes for an open
        // playlist: someone who just asked for it is about to click a row, and taking
        // the cursor away mid-reach is the same rudeness.
        var transport = this.FindControl<TransportBar>("FullscreenTransport");
        if (transport?.IsPointerOver == true ||
            (DataContext is MainViewModel { IsFloatingPlaylistVisible: true }))
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
