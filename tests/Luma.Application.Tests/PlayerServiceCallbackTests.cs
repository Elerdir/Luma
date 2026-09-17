using Luma.Application;
using Luma.Application.Tests.Fakes;
using Luma.Domain.Media;

namespace Luma.Application.Tests;

/// <summary>
/// What happens when the media backend calls back while a command is in flight.
///
/// The player serializes its state behind one gate, and commands hold that gate while
/// they call into the engine. That is safe only for as long as no thread the engine
/// itself waits for ever needs the gate — which is a property of the whole arrangement
/// rather than of any one method, and so is worth a test that fails rather than a
/// comment that is read.
/// </summary>
public sealed class PlayerServiceCallbackTests
{
    /// <summary>
    /// The deadlock this is here to prevent: libvlc 3's stop does not return until
    /// playback has halted on the thread that also delivers position ticks. Hold a
    /// command inside stop, have that thread report a position, and if the report waits
    /// for the gate then nothing can proceed — the command waits for the engine, the
    /// engine waits for its thread, and its thread waits for the command.
    ///
    /// Before the fix this test does not hang: it fails on the timeout below, which is
    /// the point of writing it this way. In the application there is no timeout.
    /// </summary>
    [Fact]
    public async Task A_position_tick_during_a_blocking_engine_call_does_not_wait_for_the_gate()
    {
        var engine = new FakeMediaEngine();
        await using var player = new PlayerService(engine);

        await player.OpenAsync(MediaSource.FromFile(Path.Combine(Path.GetTempPath(), "film.mkv")));
        engine.RaiseOpened(TimeSpan.FromMinutes(90));

        using var tickDelivered = new ManualResetEventSlim();
        var tickWasDelivered = false;

        engine.WhileStopping = () =>
        {
            // Stands in for libvlc's own thread, which stop is waiting for.
            var backend = Task.Run(() =>
            {
                engine.RaisePosition(TimeSpan.FromSeconds(42));
                tickDelivered.Set();
            });

            tickWasDelivered = tickDelivered.Wait(TimeSpan.FromSeconds(5));
            backend.Wait(TimeSpan.FromSeconds(5));
        };

        player.Stop();

        tickWasDelivered.ShouldBeTrue(
            "a position tick blocked on the gate while a command held it inside the engine — " +
            "in the application that is a hang with nothing to show for it");
    }

    /// <summary>
    /// And the ordinary case still works: a tick with nothing else going on is applied,
    /// not dropped. The non-blocking acquire must be a concession to contention, not a
    /// quiet way of losing every update.
    /// </summary>
    [Fact]
    public async Task A_position_tick_on_a_quiet_player_is_applied()
    {
        var engine = new FakeMediaEngine();
        await using var player = new PlayerService(engine);

        await player.OpenAsync(MediaSource.FromFile(Path.Combine(Path.GetTempPath(), "film.mkv")));
        engine.RaiseOpened(TimeSpan.FromMinutes(90));

        engine.RaisePosition(TimeSpan.FromSeconds(42));

        player.Snapshot.Position.ShouldBe(TimeSpan.FromSeconds(42));
    }
}
