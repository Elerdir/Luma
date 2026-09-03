using FsCheck;
using FsCheck.Xunit;
using Luma.Domain.Media;
using Luma.Domain.Playback;

namespace Luma.Domain.Tests.Playback;

/// <summary>
/// Property-based tests over <see cref="PlaybackSession"/>. Instead of one example at a
/// time, FsCheck generates a few hundred random inputs per property — durations, seek
/// targets, command sequences — and shrinks any failure down to the smallest input that
/// still breaks it. These target invariants that must hold for *every* input, which
/// complements <see cref="PlaybackStatusExtensionsTests"/>'s exhaustive but example-based
/// cross-check of the six canonical states.
/// </summary>
public class PlaybackSessionPropertyTests
{
    // Absolute on every platform; a "C:\..." literal is a relative path on Linux.
    private static readonly MediaSource Sample =
        MediaSource.FromFile(Path.Combine(Path.GetTempPath(), "luma", "clip.mp4"));

    // ---- Position / duration ----

    /// <summary>
    /// Seeking anywhere — before zero, past the end, or in between — never leaves the
    /// position negative, and never past a <em>known</em> duration. A duration of Zero
    /// is the domain's "unknown" sentinel (a network stream that has not reported a
    /// length yet), so it does not cap the upper bound — see <c>PlaybackSession.Clamp</c>.
    /// </summary>
    [Property]
    public void Seek_never_goes_negative_and_respects_a_known_duration(
        NonNegativeInt durationSeconds, int seekSeconds)
    {
        var duration = TimeSpan.FromSeconds(durationSeconds.Get);
        var session = PlayingSession(duration);

        session.Seek(TimeSpan.FromSeconds(seekSeconds));

        session.Position.ShouldBeGreaterThanOrEqualTo(TimeSpan.Zero);
        if (duration > TimeSpan.Zero)
            session.Position.ShouldBeLessThanOrEqualTo(duration);
    }

    /// <summary>
    /// The same rule holds for backend-reported position ticks. Unlike <see cref="Seek"/>
    /// these are not a user command — they must clamp silently rather than throw, since a
    /// slightly stale report racing a seek is normal.
    /// </summary>
    [Property]
    public void ReportPosition_never_goes_negative_and_respects_a_known_duration(
        NonNegativeInt durationSeconds, int reportedSeconds)
    {
        var duration = TimeSpan.FromSeconds(durationSeconds.Get);
        var session = PlayingSession(duration);

        session.ReportPosition(TimeSpan.FromSeconds(reportedSeconds));

        session.Position.ShouldBeGreaterThanOrEqualTo(TimeSpan.Zero);
        if (duration > TimeSpan.Zero)
            session.Position.ShouldBeLessThanOrEqualTo(duration);
    }

    /// <summary>A negative duration reported by the backend is floored at zero — nothing
    /// downstream (progress bars, "time remaining") expects a negative duration.</summary>
    [Property]
    public void CompleteLoad_never_leaves_a_negative_duration(int durationSeconds)
    {
        var session = new PlaybackSession();
        session.BeginLoad(Sample);
        session.CompleteLoad(TimeSpan.FromSeconds(durationSeconds));

        session.Duration.ShouldBeGreaterThanOrEqualTo(TimeSpan.Zero);
    }

    // ---- Volume / mute ----

    /// <summary>
    /// Whatever level is chosen becomes <see cref="PlaybackSession.Volume"/> outright, and
    /// choosing an audible one always lifts muting — regardless of whether the session was
    /// muted going in. <see cref="PlaybackSession.EffectiveVolume"/> (what the backend is
    /// actually told) tracks <see cref="PlaybackSession.IsMuted"/> and never drifts from it.
    /// </summary>
    [Property]
    public void ChangeVolume_updates_effective_volume_and_lifts_mute_when_audible(int level, bool startMuted)
    {
        var session = new PlaybackSession();
        session.SetMuted(startMuted);

        var volume = Volume.Of(level);
        session.ChangeVolume(volume);

        session.Volume.ShouldBe(volume);
        session.IsMuted.ShouldBe(startMuted && volume.IsMuted);
        session.EffectiveVolume.ShouldBe(session.IsMuted ? Volume.Muted : volume);
    }

    // ---- Command-sequence (model-based) invariants ----

    /// <summary>
    /// Drives the aggregate through a random walk of commands — including ones illegal
    /// in the current state, which are simply rejected — and checks invariants that must
    /// hold after *every* step. The six canonical states in
    /// <see cref="PlaybackStatusExtensionsTests"/> are each reached by one hand-written
    /// setup; this instead reaches whatever states hundreds of random histories produce,
    /// which is what would actually catch a field left stale by some order of calls
    /// nobody wrote a targeted example for.
    /// </summary>
    [Property]
    public void Random_command_sequences_never_violate_invariants(NonEmptyArray<int> steps)
    {
        var session = new PlaybackSession();

        foreach (var raw in steps.Get)
        {
            Apply(session, ToCommand(raw));

            session.Position.ShouldBeGreaterThanOrEqualTo(TimeSpan.Zero, "Position went negative");
            if (session.Duration > TimeSpan.Zero)
                session.Position.ShouldBeLessThanOrEqualTo(session.Duration, "Position ran past a known Duration");

            var expectedEffective = session.IsMuted ? Volume.Muted : session.Volume;
            session.EffectiveVolume.ShouldBe(expectedEffective, "EffectiveVolume drifted from IsMuted/Volume");

            if (session.Status is PlaybackStatus.NoMedia)
            {
                session.Source.ShouldBeNull("Stop must clear the source");
                session.Duration.ShouldBe(TimeSpan.Zero, "Stop must clear the duration");
                session.AudioTracks.ShouldBeEmpty("Stop must clear audio tracks");
                session.SubtitleTracks.ShouldBeEmpty("Stop must clear subtitle tracks");
                session.SelectedAudioTrack.ShouldBeNull("Stop must clear the audio selection");
                session.SelectedSubtitleTrack.ShouldBeNull("Stop must clear the subtitle selection");
            }
        }
    }

    private enum Command
    {
        Play, Pause, Toggle, Stop, SeekZero, SeekPastEnd,
        ReportEnd, Fault, Mute, Unmute, ToggleMute, Reload
    }

    /// <summary>Maps any int FsCheck hands us — including negative ones — onto a command.</summary>
    private static Command ToCommand(int raw) => (Command)(((raw % 12) + 12) % 12);

    private static void Apply(PlaybackSession session, Command command)
    {
        try
        {
            switch (command)
            {
                case Command.Play: session.Play(); break;
                case Command.Pause: session.Pause(); break;
                case Command.Toggle: session.TogglePlayPause(); break;
                case Command.Stop: session.Stop(); break;
                case Command.SeekZero: session.Seek(TimeSpan.Zero); break;
                case Command.SeekPastEnd: session.Seek(session.Duration + TimeSpan.FromMinutes(1)); break;
                case Command.ReportEnd: session.ReportEnded(); break;
                case Command.Fault: session.Fault("boom"); break;
                case Command.Mute: session.SetMuted(true); break;
                case Command.Unmute: session.SetMuted(false); break;
                case Command.ToggleMute: session.ToggleMute(); break;
                case Command.Reload:
                    session.BeginLoad(Sample);
                    session.CompleteLoad(TimeSpan.FromMinutes(3));
                    break;
            }
        }
        catch (InvalidPlaybackTransitionException)
        {
            // Expected noise in a random walk — e.g. Pause() while NoMedia. The
            // invariants above are what matters, not which command happened to succeed.
        }
    }

    private static PlaybackSession PlayingSession(TimeSpan duration)
    {
        var session = new PlaybackSession();
        session.BeginLoad(Sample);
        session.CompleteLoad(duration, autoPlay: true);
        return session;
    }
}
