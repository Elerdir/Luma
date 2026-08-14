using Luma.Presentation.Localization;
using Luma.Presentation.Services;
using Luma.Presentation.Tests.Fakes;
using Luma.Presentation.ViewModels;

namespace Luma.Presentation.Tests;

/// <summary>
/// Shares the localizer singleton with <see cref="LocalizerTests"/>, so it joins the
/// same non-parallel collection and puts the language back afterwards.
/// </summary>
[Collection(nameof(LocalizerTests))]
public sealed class MainViewModelDisposalTests : IDisposable
{
    private readonly string _originalLanguage = Localizer.Instance.CurrentLanguage;

    public void Dispose() => Localizer.Instance.SetLanguage(_originalLanguage);

    private static MainViewModel Create(FakePlayer player) =>
        new(player, new FakeFilePicker(), new FakeUpdateService(), new FakeInstallerLauncher(),
            new InterfaceOptionsService(new FakeSettingsStore<InterfaceOptions>()));

    [Fact]
    public void Disposing_lets_go_of_the_player()
    {
        var player = new FakePlayer();
        var viewModel = Create(player);
        player.Subscribers.ShouldBe(1);

        viewModel.Dispose();

        player.Subscribers.ShouldBe(0);
    }

    [Fact]
    public void A_disposed_view_model_stops_reacting_to_the_language()
    {
        var player = new FakePlayer();
        var viewModel = Create(player);

        Localizer.Instance.SetLanguage("en");
        viewModel.Dispose();
        Localizer.Instance.SetLanguage("cs");

        // Still the English text it last built: the singleton no longer reaches it.
        viewModel.StatusText.ShouldBe("Ready");
    }

    [Fact]
    public void A_disposed_view_model_stops_reacting_to_the_player()
    {
        var player = new FakePlayer();
        var viewModel = Create(player);
        viewModel.Dispose();

        Should.NotThrow(player.Publish);
    }

    [Fact]
    public void Disposing_twice_is_harmless()
    {
        var player = new FakePlayer();
        var viewModel = Create(player);

        viewModel.Dispose();
        Should.NotThrow(viewModel.Dispose);
        player.Subscribers.ShouldBe(0);
    }
}
