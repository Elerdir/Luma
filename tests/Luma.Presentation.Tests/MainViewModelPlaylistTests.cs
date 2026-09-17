using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Luma.Application;
using Luma.Domain.Media;
using Luma.Domain.Playback;
using Luma.Domain.Playlists;
using Luma.Presentation.Localization;
using Luma.Presentation.Services;
using Luma.Presentation.Tests.Fakes;
using Luma.Presentation.ViewModels;

namespace Luma.Presentation.Tests;

/// <summary>
/// Reordering (the Move buttons and <see cref="MainViewModel.MovePlaylistEntry"/>, which
/// backs dragging a row) and .m3u save/load — all driven from
/// <see cref="MainViewModel.Playlist"/>, so these need the same headless dispatcher pump
/// as <see cref="MainViewModelDispatchTests"/> to populate it from a snapshot.
/// </summary>
[Collection(nameof(LocalizerTests))]
public sealed class MainViewModelPlaylistTests : IDisposable
{
    private readonly string _originalLanguage = Localizer.Instance.CurrentLanguage;

    public MainViewModelPlaylistTests() => Localizer.Instance.SetLanguage("en");

    public void Dispose() => Localizer.Instance.SetLanguage(_originalLanguage);

    private static MediaSource File(string name) =>
        MediaSource.FromFile(Path.Combine(Path.GetTempPath(), "luma", name));

    private static (MainViewModel ViewModel, FakePlayer Player, FakeFilePicker Picker, FakePlaylistFileStore Store) Create()
    {
        var player = new FakePlayer();
        var picker = new FakeFilePicker();
        var store = new FakePlaylistFileStore();
        var viewModel = new MainViewModel(
            player, picker, new FakeUpdateService(), new FakeInstallerLauncher(),
            new InterfaceOptionsService(new FakeSettingsStore<InterfaceOptions>()),
            store);
        return (viewModel, player, picker, store);
    }

    /// <summary>Publishes a three-entry playlist and pumps the dispatcher so
    /// <see cref="MainViewModel.Playlist"/> is populated from it.</summary>
    private static void PublishThreeEntryPlaylist(FakePlayer player)
    {
        var items = new[] { File("a.mkv"), File("b.mkv"), File("c.mkv") };
        player.Publish(new PlayerSnapshot(
            PlaybackStatus.Playing, items[0], items[0].DisplayName,
            TimeSpan.Zero, TimeSpan.FromMinutes(5),
            Volume.Default, false, PlaybackRate.Normal, null,
            items.Length, 0, items, RepeatMode.None, [], [], null, null));
        Dispatcher.UIThread.RunJobs();
    }

    // ---- Move Up / Move Down ----

    [AvaloniaFact]
    public void Moving_the_middle_entry_up_asks_the_player_to_swap_it_with_its_predecessor()
    {
        var (vm, player, _, _) = Create();
        PublishThreeEntryPlaylist(player);
        vm.SelectedPlaylistItem = vm.Playlist[1];

        vm.MoveSelectedUpCommand.Execute(null);

        player.MoveCalls.ShouldHaveSingleItem().ShouldBe((1, 0));
    }

    [AvaloniaFact]
    public void Moving_the_middle_entry_down_asks_the_player_to_swap_it_with_its_successor()
    {
        var (vm, player, _, _) = Create();
        PublishThreeEntryPlaylist(player);
        vm.SelectedPlaylistItem = vm.Playlist[1];

        vm.MoveSelectedDownCommand.Execute(null);

        player.MoveCalls.ShouldHaveSingleItem().ShouldBe((1, 2));
    }

    [AvaloniaFact]
    public void Boundary_entries_disable_the_matching_move_command()
    {
        var (vm, player, _, _) = Create();
        PublishThreeEntryPlaylist(player);

        vm.SelectedPlaylistItem = vm.Playlist[0];
        vm.MoveSelectedUpCommand.CanExecute(null).ShouldBeFalse();
        vm.MoveSelectedDownCommand.CanExecute(null).ShouldBeTrue();

        vm.SelectedPlaylistItem = vm.Playlist[2];
        vm.MoveSelectedUpCommand.CanExecute(null).ShouldBeTrue();
        vm.MoveSelectedDownCommand.CanExecute(null).ShouldBeFalse();
    }

    // ---- Dragging a row (MovePlaylistEntry) ----

    [AvaloniaFact]
    public void Dragging_a_row_onto_another_moves_it_there()
    {
        var (vm, player, _, _) = Create();
        PublishThreeEntryPlaylist(player);

        vm.MovePlaylistEntry(vm.Playlist[0], 2);

        player.MoveCalls.ShouldHaveSingleItem().ShouldBe((0, 2));
    }

    [AvaloniaFact]
    public void Dropping_a_row_onto_its_own_position_does_nothing()
    {
        var (vm, player, _, _) = Create();
        PublishThreeEntryPlaylist(player);

        vm.MovePlaylistEntry(vm.Playlist[1], 1);

        player.MoveCalls.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public void Dropping_a_row_no_longer_in_the_playlist_does_nothing()
    {
        var (vm, player, _, _) = Create();
        PublishThreeEntryPlaylist(player);
        var stray = new PlaylistItemViewModel(File("gone.mkv"));

        vm.MovePlaylistEntry(stray, 0);

        player.MoveCalls.ShouldBeEmpty();
    }

    // ---- Save ----

    [AvaloniaFact]
    public async Task Saving_writes_the_current_playlist_to_the_chosen_path()
    {
        var (vm, player, picker, store) = Create();
        PublishThreeEntryPlaylist(player);
        picker.PlaylistSavePath = Path.Combine(Path.GetTempPath(), "luma", "saved.m3u");

        await vm.SavePlaylistCommand.ExecuteAsync(null);

        store.Saved.ShouldContainKey(picker.PlaylistSavePath);
        store.Saved[picker.PlaylistSavePath].ShouldBe(vm.Playlist.Select(i => i.Source).ToArray());
    }

    [AvaloniaFact]
    public async Task Saving_an_empty_playlist_never_prompts_for_a_path()
    {
        var (vm, _, picker, _) = Create();

        await vm.SavePlaylistCommand.ExecuteAsync(null);

        picker.SaveDialogRequests.ShouldBe(0);
    }

    [AvaloniaFact]
    public async Task Cancelling_the_save_dialog_writes_nothing()
    {
        var (vm, player, picker, store) = Create();
        PublishThreeEntryPlaylist(player);
        picker.PlaylistSavePath = null; // cancelled

        await vm.SavePlaylistCommand.ExecuteAsync(null);

        store.Saved.ShouldBeEmpty();
    }

    // ---- Load ----

    [AvaloniaFact]
    public async Task Loading_opens_what_the_store_returns_for_the_chosen_path()
    {
        var (vm, player, picker, store) = Create();
        var path = Path.Combine(Path.GetTempPath(), "luma", "show.m3u");
        var items = new[] { File("ep1.mkv"), File("ep2.mkv") };
        picker.PlaylistToOpen = path;
        store.ToLoad[path] = items;

        await vm.LoadPlaylistCommand.ExecuteAsync(null);

        // As a list, not as a selection of files: the difference decides whether a
        // playlist of one drags its whole folder in behind it.
        player.OpenedAsPlaylist.ShouldHaveSingleItem().ShouldBe(items);
        player.OpenedPlaylists.ShouldBeEmpty();
    }

    /// <summary>
    /// The case that made the distinction necessary. Nothing about one entry makes a
    /// playlist into a file somebody opened.
    /// </summary>
    [AvaloniaFact]
    public async Task A_playlist_holding_one_film_is_still_opened_as_a_list()
    {
        var (vm, player, picker, store) = Create();
        var path = Path.Combine(Path.GetTempPath(), "luma", "one.m3u");
        var items = new[] { File("ep1.mkv") };
        picker.PlaylistToOpen = path;
        store.ToLoad[path] = items;

        await vm.LoadPlaylistCommand.ExecuteAsync(null);

        player.OpenedAsPlaylist.ShouldHaveSingleItem().ShouldBe(items);
        player.OpenedPlaylists.ShouldBeEmpty();
    }

    /// <summary>
    /// A playlist whose entries all failed to resolve — a file moved, a format this
    /// build cannot read. Opening nothing used to surface as "At least one source is
    /// required", which is a sentence written for whoever wrote the method.
    /// </summary>
    [AvaloniaFact]
    public async Task An_empty_playlist_says_so_in_words_meant_for_a_person()
    {
        var (vm, player, picker, store) = Create();
        var path = Path.Combine(Path.GetTempPath(), "luma", "empty.m3u");
        picker.PlaylistToOpen = path;
        store.ToLoad[path] = [];

        await vm.LoadPlaylistCommand.ExecuteAsync(null);

        player.OpenedAsPlaylist.ShouldBeEmpty();
        player.OpenedPlaylists.ShouldBeEmpty();
        vm.StatusText.ShouldBe("That playlist has nothing playable in it.");
    }

    [AvaloniaFact]
    public async Task Cancelling_the_open_dialog_opens_nothing()
    {
        var (vm, player, picker, _) = Create();
        picker.PlaylistToOpen = null;

        await vm.LoadPlaylistCommand.ExecuteAsync(null);

        player.OpenedPlaylists.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task A_load_failure_is_reported_rather_than_thrown()
    {
        var (vm, player, picker, _) = Create();
        picker.PlaylistToOpen = Path.Combine(Path.GetTempPath(), "luma", "missing.m3u");
        // Deliberately not registered in store.ToLoad, so LoadAsync throws.

        await vm.LoadPlaylistCommand.ExecuteAsync(null);

        vm.StatusText.ShouldStartWith("Error:");
        player.OpenedPlaylists.ShouldBeEmpty();
    }
}
