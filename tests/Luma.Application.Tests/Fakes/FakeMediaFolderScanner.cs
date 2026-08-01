using Luma.Application.Abstractions;
using Luma.Domain.Media;

namespace Luma.Application.Tests.Fakes;

/// <summary>Returns a fixed folder listing regardless of the media asked about.</summary>
public sealed class FakeMediaFolderScanner(params MediaSource[] siblings) : IMediaFolderScanner
{
    public List<MediaSource> Queries { get; } = [];

    public IReadOnlyList<MediaSource> FindSiblingsOf(MediaSource media)
    {
        Queries.Add(media);
        return siblings;
    }
}
