using Luma.Domain.Playback;
using Luma.Infrastructure.Media;

namespace Luma.Infrastructure.Tests;

/// <summary>
/// Integration smoke tests that touch the native LibVLC libraries. Filtered out of
/// headless CI with: <c>dotnet test --filter Category!=Integration</c>.
/// </summary>
[Trait("Category", "Integration")]
public class LibVlcMediaEngineTests
{
    [Fact]
    public async Task Can_construct_and_dispose_engine()
    {
        var engine = new LibVlcMediaEngine();
        await engine.DisposeAsync();
    }

    [Fact]
    public async Task Volume_and_rate_calls_do_not_throw_before_media()
    {
        await using var engine = new LibVlcMediaEngine();

        Should.NotThrow(() => engine.SetVolume(Volume.Of(50)));
        Should.NotThrow(() => engine.SetRate(PlaybackRate.Of(1.25)));
    }

    [Fact]
    public async Task Player_surface_is_exposed_for_video_view()
    {
        await using var engine = new LibVlcMediaEngine();
        engine.Player.ShouldNotBeNull();
    }
}
