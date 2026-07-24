namespace Luma.Domain.Media;

/// <summary>The kind of selectable stream inside a media file.</summary>
public enum TrackKind
{
    Audio,
    Subtitle
}

/// <summary>
/// A selectable audio or subtitle stream. <see cref="Id"/> is the backend's
/// identifier for the stream; equality is by value.
/// </summary>
public sealed record MediaTrack(int Id, string Name, TrackKind Kind)
{
    public static MediaTrack Audio(int id, string name) => new(id, Describe(name, id), TrackKind.Audio);
    public static MediaTrack Subtitle(int id, string name) => new(id, Describe(name, id), TrackKind.Subtitle);

    private static string Describe(string name, int id) =>
        string.IsNullOrWhiteSpace(name) ? $"Track {id}" : name;

    public override string ToString() => Name;
}
