using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Luma.Presentation.Input;
using Luma.Presentation.Localization;
using Luma.Presentation.ViewModels;

namespace Luma.Presentation.Services;

/// <summary>
/// The menu bar along the top of the screen on macOS.
///
/// Every command here is also in the right-click menu over the video, so nothing
/// becomes reachable that was not before. What changes is that Luma stops looking like
/// a program from somewhere else: a Mac application with only an automatic "Quit" in
/// its menu bar is one nobody has finished.
///
/// Built in code rather than declared in App.axaml because the commands live on the
/// view-model, which does not exist until the composition root has run.
/// </summary>
public static class ApplicationMenu
{
    /// <summary>
    /// Give the application its menu bar, where the platform has one.
    ///
    /// Windows and Linux put menus in the window, and Luma has no window menu — so on
    /// those this does nothing at all rather than adding a bar nobody asked for.
    /// </summary>
    public static void AttachTo(Avalonia.Application application, MainViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(viewModel);

        if (!OperatingSystem.IsMacOS())
            return;

        NativeMenu.SetMenu(application, Build(viewModel));
    }

    /// <summary>The menu itself, separate from the platform check so it can be inspected.</summary>
    public static NativeMenu Build(MainViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        // macOS puts the application's own menu — About, Hide, Quit — first and manages
        // it itself. These follow it.
        var file = Submenu("Menu.File",
            Item("Menu.Open", viewModel.OpenCommand, PlayerShortcuts.OpenGesture),
            Item("Menu.LoadSubtitle", viewModel.LoadSubtitleCommand));

        var playback = Submenu("Menu.Playback",
            Item("Menu.PlayPause", viewModel.PlayPauseCommand, new KeyGesture(Key.Space)),
            Item("Menu.Stop", viewModel.StopCommand, new KeyGesture(Key.S)),
            Separator(),
            Item("Menu.Previous", viewModel.PreviousCommand, new KeyGesture(Key.P)),
            Item("Menu.Next", viewModel.NextCommand, new KeyGesture(Key.N)));

        var view = Submenu("Menu.View",
            Item("Menu.Playlist", viewModel.TogglePlaylistCommand, new KeyGesture(Key.L)));

        return new NativeMenu { Items = { file, playback, view } };
    }

    private static NativeMenuItem Submenu(string key, params NativeMenuItemBase[] items)
    {
        var menu = new NativeMenu();
        foreach (var item in items)
            menu.Items.Add(item);

        var header = Localized(key);
        header.Menu = menu;
        return header;
    }

    private static NativeMenuItemSeparator Separator() => new();

    private static NativeMenuItem Item(string key, ICommand command, KeyGesture? gesture = null)
    {
        var item = Localized(key);
        item.Command = command;
        item.Gesture = gesture;
        return item;
    }

    /// <summary>
    /// A menu item whose text follows the chosen language. Bound rather than assigned:
    /// switching language is meant to take effect without a restart, and a header set
    /// once would keep whatever language the application started in.
    /// </summary>
    private static NativeMenuItem Localized(string key)
    {
        var item = new NativeMenuItem();

        item.Bind(NativeMenuItem.HeaderProperty, new Binding(nameof(LocalizedString.Value))
        {
            Source = new LocalizedString(key),
            Mode = BindingMode.OneWay
        });

        return item;
    }
}
