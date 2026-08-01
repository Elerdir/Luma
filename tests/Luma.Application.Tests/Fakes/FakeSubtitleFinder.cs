using Luma.Application.Abstractions;
using Luma.Domain.Media;

namespace Luma.Application.Tests.Fakes;

/// <summary>Returns a fixed set of sidecar subtitles regardless of the media.</summary>
public sealed class FakeSubtitleFinder(params MediaSource[] results) : ISubtitleFinder
{
    public List<MediaSource> Queries { get; } = [];

    public IReadOnlyList<MediaSource> FindFor(MediaSource media)
    {
        Queries.Add(media);
        return results;
    }
}
