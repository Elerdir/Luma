using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Luma.Presentation.ViewModels;

namespace Luma.Presentation.Controls;

/// <summary>
/// The playlist. See PlaylistPanel.axaml for why this is a control rather than markup
/// sitting directly in the window.
/// </summary>
public partial class PlaylistPanel : UserControl
{
    private readonly ListBox _rows;

    public PlaylistPanel()
    {
        InitializeComponent();
        _rows = this.FindControl<ListBox>("Rows")!;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>Double-clicking a row plays it, the usual convention.</summary>
    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.PlaySelectedCommand.CanExecute(null))
            vm.PlaySelectedCommand.Execute(null);

        e.Handled = true;
    }

    // ---- Drag-to-reorder ----
    //
    // Deliberately not Avalonia's DragDrop.DoDragDrop: the whole gesture starts and
    // ends inside this one ListBox, so there is no need for an OS-level drag (a drop
    // cursor, cross-window targets) — tracking the pointer and re-hit-testing where it
    // ends up is simpler and cannot conflict with a row's own click/selection handling,
    // which has already run by the time PointerPressed reaches here.
    //
    // A move only commits on release, and only once the pointer has travelled past
    // DragThreshold — otherwise an ordinary click that happens to land a pixel off
    // center would occasionally "move" a row onto itself.

    private const double DragThreshold = 6;

    private PlaylistItemViewModel? _dragItem;
    private Point _dragStart;
    private bool _dragging;

    private void OnRowsPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_rows).Properties.IsLeftButtonPressed)
            return;

        _dragItem = RowAt(e.Source);
        _dragStart = e.GetPosition(_rows);
        _dragging = false;
    }

    private void OnRowsPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragItem is null || _dragging)
            return;

        if (!e.GetCurrentPoint(_rows).Properties.IsLeftButtonPressed)
        {
            _dragItem = null;
            return;
        }

        var position = e.GetPosition(_rows);
        if (Math.Abs(position.Y - _dragStart.Y) < DragThreshold)
            return;

        _dragging = true;
        e.Pointer.Capture(_rows);
        Cursor = new Cursor(StandardCursorType.SizeAll);
    }

    private void OnRowsPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragging && DataContext is MainViewModel vm && _dragItem is { } source)
        {
            var target = RowAtPoint(e.GetPosition(_rows));
            if (target is not null && !ReferenceEquals(target, source))
                vm.MovePlaylistEntry(source, vm.Playlist.IndexOf(target));
        }

        EndDrag();
    }

    private void OnRowsPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => EndDrag();

    private void EndDrag()
    {
        _dragItem = null;
        _dragging = false;
        Cursor = Cursor.Default;
    }

    /// <summary>The row a routed event's original source landed on, if any.</summary>
    private static PlaylistItemViewModel? RowAt(object? eventSource) =>
        (eventSource as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext
            as PlaylistItemViewModel;

    /// <summary>The row currently under a point in the list's own coordinates.</summary>
    private PlaylistItemViewModel? RowAtPoint(Point position) =>
        RowAt(_rows.InputHitTest(position));
}
