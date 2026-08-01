using Luma.Domain.Media;
using Luma.Domain.Playback;

namespace Luma.Domain.Tests.Playback;

/// <summary>
/// The predicates must agree with what the aggregate actually accepts — a mismatch
/// means the UI either enables a control that throws, or disables a legal one.
/// </summary>
public class PlaybackStatusExtensionsTests
{
    [Theory]
    [InlineData(PlaybackStatus.NoMedia, false)]
    [InlineData(PlaybackStatus.Loading, false)]
    [InlineData(PlaybackStatus.Playing, true)]
    [InlineData(PlaybackStatus.Paused, true)]
    [InlineData(PlaybackStatus.Ended, true)]
    [InlineData(PlaybackStatus.Faulted, false)]
    public void CanPlay_matches_expected_states(PlaybackStatus status, bool expected) =>
        status.CanPlay().ShouldBe(expected);

    [Theory]
    [InlineData(PlaybackStatus.NoMedia, false)]
    [InlineData(PlaybackStatus.Loading, false)]
    [InlineData(PlaybackStatus.Playing, true)]
    [InlineData(PlaybackStatus.Paused, true)]
    [InlineData(PlaybackStatus.Ended, false)]
    [InlineData(PlaybackStatus.Faulted, false)]
    public void CanPause_matches_expected_states(PlaybackStatus status, bool expected) =>
        status.CanPause().ShouldBe(expected);

    [Theory]
    [InlineData(PlaybackStatus.NoMedia, false)]
    [InlineData(PlaybackStatus.Loading, false)]
    [InlineData(PlaybackStatus.Playing, true)]
    [InlineData(PlaybackStatus.Paused, true)]
    [InlineData(PlaybackStatus.Ended, true)]
    [InlineData(PlaybackStatus.Faulted, false)]
    public void CanSeek_matches_expected_states(PlaybackStatus status, bool expected) =>
        status.CanSeek().ShouldBe(expected);

    [Theory]
    [InlineData(PlaybackStatus.NoMedia, false)]
    [InlineData(PlaybackStatus.Loading, true)]
    [InlineData(PlaybackStatus.Playing, true)]
    [InlineData(PlaybackStatus.Paused, true)]
    [InlineData(PlaybackStatus.Ended, true)]
    [InlineData(PlaybackStatus.Faulted, true)]
    public void CanStop_matches_expected_states(PlaybackStatus status, bool expected) =>
        status.CanStop().ShouldBe(expected);

    /// <summary>
    /// Exhaustively cross-checks the throwing operations against their predicate:
    /// whenever a predicate says yes the operation must not throw, and whenever it
    /// says no it must. <c>Stop</c> is excluded — it never throws (it is a no-op with
    /// no media), so <see cref="PlaybackStatusExtensions.CanStop"/> is purely about
    /// whether there is anything worth stopping.
    /// </summary>
    [Theory]
    [InlineData(PlaybackStatus.NoMedia)]
    [InlineData(PlaybackStatus.Loading)]
    [InlineData(PlaybackStatus.Playing)]
    [InlineData(PlaybackStatus.Paused)]
    [InlineData(PlaybackStatus.Ended)]
    [InlineData(PlaybackStatus.Faulted)]
    public void Predicates_agree_with_the_aggregate(PlaybackStatus status)
    {
        AssertAgrees(status, s => s.CanPlay, s => s.Play());
        AssertAgrees(status, s => s.CanPause, s => s.Pause());
        AssertAgrees(status, s => s.CanTogglePlayPause, s => s.TogglePlayPause());
        AssertAgrees(status, s => s.CanSeek, s => s.Seek(TimeSpan.FromSeconds(1)));
    }

    private static void AssertAgrees(
        PlaybackStatus status,
        Func<PlaybackSession, bool> predicate,
        Action<PlaybackSession> operation)
    {
        var session = SessionIn(status);
        var allowed = predicate(session);

        if (allowed)
            Should.NotThrow(() => operation(session));
        else
            Should.Throw<InvalidPlaybackTransitionException>(() => operation(session));
    }

    private static PlaybackSession SessionIn(PlaybackStatus status)
    {
        // Absolute on every platform; a "C:\..." literal is a relative path on Linux.
        var source = MediaSource.FromFile(Path.Combine(Path.GetTempPath(), "luma", "clip.mp4"));
        var session = new PlaybackSession();

        switch (status)
        {
            case PlaybackStatus.NoMedia:
                break;
            case PlaybackStatus.Loading:
                session.BeginLoad(source);
                break;
            case PlaybackStatus.Playing:
                session.BeginLoad(source);
                session.CompleteLoad(TimeSpan.FromMinutes(1));
                break;
            case PlaybackStatus.Paused:
                session.BeginLoad(source);
                session.CompleteLoad(TimeSpan.FromMinutes(1), autoPlay: false);
                break;
            case PlaybackStatus.Ended:
                session.BeginLoad(source);
                session.CompleteLoad(TimeSpan.FromMinutes(1));
                session.ReportEnded();
                break;
            case PlaybackStatus.Faulted:
                session.BeginLoad(source);
                session.Fault("boom");
                break;
        }

        session.Status.ShouldBe(status);
        return session;
    }
}
