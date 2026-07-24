namespace Luma.Domain.Playback;

/// <summary>
/// Playback speed multiplier, constrained to [0.25, 4.0].
/// 1.0 is normal speed. Immutable value object.
/// </summary>
public readonly record struct PlaybackRate
{
    public const double Min = 0.25;
    public const double Max = 4.0;

    public double Multiplier { get; }

    private PlaybackRate(double multiplier) => Multiplier = multiplier;

    /// <summary>Creates a rate, clamping the input into the valid range.</summary>
    public static PlaybackRate Of(double multiplier) => new(Math.Clamp(multiplier, Min, Max));

    public static PlaybackRate Normal => new(1.0);

    public bool IsNormal => Math.Abs(Multiplier - 1.0) < 0.0001;

    public override string ToString() => $"{Multiplier:0.##}x";
}
