using Luma.Domain.Media;

namespace Luma.Domain.Playlists;

/// <summary>
/// An ordered list of media with a notion of the "current" item and
/// repeat-aware navigation. Pure domain logic — no playback side effects.
/// </summary>
public sealed class Playlist
{
    private readonly List<MediaSource> _items = [];

    public IReadOnlyList<MediaSource> Items => _items;
    public int CurrentIndex { get; private set; } = -1;
    public RepeatMode Repeat { get; set; } = RepeatMode.None;

    public int Count => _items.Count;
    public bool IsEmpty => _items.Count == 0;
    public MediaSource? Current =>
        CurrentIndex >= 0 && CurrentIndex < _items.Count ? _items[CurrentIndex] : null;

    /// <summary>Append an item. The first item added becomes current.</summary>
    public void Add(MediaSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _items.Add(source);
        if (CurrentIndex < 0)
            CurrentIndex = 0;
    }

    public void AddRange(IEnumerable<MediaSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        foreach (var s in sources)
            Add(s);
    }

    /// <summary>Remove the item at <paramref name="index"/>, keeping the current selection stable.</summary>
    public void RemoveAt(int index)
    {
        if (index < 0 || index >= _items.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        _items.RemoveAt(index);

        if (_items.Count == 0)
            CurrentIndex = -1;
        else if (index < CurrentIndex)
            CurrentIndex--;
        else if (index == CurrentIndex && CurrentIndex >= _items.Count)
            CurrentIndex = _items.Count - 1;
    }

    public void Clear()
    {
        _items.Clear();
        CurrentIndex = -1;
    }

    /// <summary>Select an explicit item by index.</summary>
    public void JumpTo(int index)
    {
        if (index < 0 || index >= _items.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        CurrentIndex = index;
    }

    /// <summary>
    /// Advance to the next item honoring <see cref="Repeat"/>.
    /// Returns false when there is nothing further to play.
    /// </summary>
    public bool MoveNext()
    {
        if (IsEmpty) return false;
        if (Repeat == RepeatMode.One) return true; // stay put, replay

        if (CurrentIndex + 1 < _items.Count)
        {
            CurrentIndex++;
            return true;
        }

        if (Repeat == RepeatMode.All)
        {
            CurrentIndex = 0;
            return true;
        }

        return false;
    }

    /// <summary>Move to the previous item honoring <see cref="Repeat"/>.</summary>
    public bool MovePrevious()
    {
        if (IsEmpty) return false;
        if (Repeat == RepeatMode.One) return true;

        if (CurrentIndex - 1 >= 0)
        {
            CurrentIndex--;
            return true;
        }

        if (Repeat == RepeatMode.All)
        {
            CurrentIndex = _items.Count - 1;
            return true;
        }

        return false;
    }
}
