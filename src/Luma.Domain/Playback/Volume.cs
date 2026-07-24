namespace Luma.Domain.Playback;

/// <summary>
/// Playback volume as a percentage in the range [0, 100].
/// Immutable value object; construction always yields a valid, clamped value.
/// </summary>
public readonly record struct Volume
{
    public const int Min = 0;
    public const int Max = 100;

    public int Level { get; }

    private Volume(int level) => Level = level;

    /// <summary>Creates a volume, clamping the input into the valid range.</summary>
    public static Volume Of(int level) => new(Math.Clamp(level, Min, Max));

    public static Volume Muted => new(Min);
    public static Volume Full => new(Max);
    public static Volume Default => Of(80);

    public bool IsMuted => Level == Min;

    public Volume Increase(int by) => Of(Level + by);
    public Volume Decrease(int by) => Of(Level - by);

    public override string ToString() => $"{Level}%";
}
