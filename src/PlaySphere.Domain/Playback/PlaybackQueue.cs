namespace PlaySphere.Domain.Playback;

/// <summary>
/// A playback session queue (PICKLE.md §25: queue, shuffle, repeat one, repeat all, play next).
/// Owns an ordered collection of items and a cursor, exposing next/previous navigation that honors
/// the current <see cref="PlaybackRepeat"/> mode, plus optional shuffle of the play order.
///
/// This is deliberately a pure, deterministic-in-input type (apart from the supplied
/// <see cref="Random"/> used only for shuffle) and carries no media/player knowledge, so it can be
/// unit-tested in isolation and reused by playlists, M3U imports and bookmark-driven sessions.
/// </summary>
/// <typeparam name="T">The queued item (e.g. a media item id or file path).</typeparam>
public sealed class PlaybackQueue<T>
{
    private readonly List<T> _items;
    private readonly List<int> _order = new();
    private int _cursor = -1;

    public PlaybackQueue() : this(Array.Empty<T>())
    {
    }

    public PlaybackQueue(IEnumerable<T> items)
    {
        _items = items?.ToList() ?? new List<T>();
        ResetOrder();
    }

    public int Count => _items.Count;

    public bool IsEmpty => _items.Count == 0;

    /// <summary>The position (0-based) of the cursor within the play order, or -1 when nothing is queued yet.</summary>
    public int Cursor => _cursor;

    public bool IsShuffled { get; private set; }

    public PlaybackRepeat RepeatMode { get; set; } = PlaybackRepeat.Off;

    /// <summary>The currently selected item, or default when the queue is empty or has ended (Off mode past the last item).</summary>
    public T? Current
    {
        get
        {
            if (_cursor < 0 || _cursor >= _order.Count)
            {
                return default;
            }

            return _items[_order[_cursor]];
        }
    }

    /// <summary>The 0-based index of the current item in the natural (unshuffled) order, or -1.</summary>
    public int CurrentIndex
    {
        get
        {
            if (_cursor < 0 || _cursor >= _order.Count)
            {
                return -1;
            }

            return _order[_cursor];
        }
    }

    /// <summary>The items in their natural (unshuffled) order.</summary>
    public IReadOnlyList<T> Items => _items;

    /// <summary>The items in the current play order, honoring shuffle.</summary>
    public IReadOnlyList<T> PlayOrder => _order.Select(i => _items[i]).ToList();

    /// <summary>Adds an item to the end of the queue. Does not change the current item.</summary>
    public void Append(T item)
    {
        _items.Add(item);
        _order.Add(_items.Count - 1);
    }

    /// <summary>Sets the queue to start playback from the first item in play order.</summary>
    public void Start()
    {
        _cursor = _order.Count > 0 ? 0 : -1;
    }

    /// <summary>Selects a specific item. Returns false when the item is not in the queue.</summary>
    public bool JumpTo(T item)
    {
        var index = _items.IndexOf(item);
        if (index < 0)
        {
            return false;
        }

        var position = _order.IndexOf(index);
        if (position < 0)
        {
            return false;
        }

        _cursor = position;
        return true;
    }

    /// <summary>
    /// Advances to the next item and returns it. Honors <see cref="PlaybackRepeat"/>:
    /// <see cref="PlaybackRepeat.One"/> returns the current item again;
    /// <see cref="PlaybackRepeat.All"/> wraps from the end back to the start;
    /// <see cref="PlaybackRepeat.Off"/> returns default once the last item has been passed.
    /// </summary>
    public T? MoveNext()
    {
        if (IsEmpty)
        {
            return default;
        }

        if (_cursor < 0)
        {
            _cursor = 0;
            return Current;
        }

        if (RepeatMode == PlaybackRepeat.One)
        {
            return Current;
        }

        if (_cursor < _order.Count - 1)
        {
            _cursor++;
            return Current;
        }

        if (RepeatMode == PlaybackRepeat.All)
        {
            _cursor = 0;
            return Current;
        }

        // Off mode: mark past-the-end.
        _cursor = _order.Count;
        return default;
    }

    /// <summary>Moves to the previous item and returns it, wrapping to the last item from the first.</summary>
    public T? MovePrevious()
    {
        if (IsEmpty)
        {
            return default;
        }

        if (RepeatMode == PlaybackRepeat.One && _cursor >= 0)
        {
            return Current;
        }

        if (_cursor <= 0)
        {
            _cursor = _order.Count - 1;
            return Current;
        }

        _cursor--;
        return Current;
    }

    /// <summary>
    /// Enables shuffle: keeps the current item at the front of the play order (so playback continues
    /// from what's playing) and randomly reorders the remaining items. Uses <paramref name="random"/>
    /// for the permutation so tests can seed it.
    /// </summary>
    public void Shuffle(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (_order.Count <= 1)
        {
            return;
        }

        var currentIndex = CurrentIndex;

        // Reorder the natural indices with a Fisher-Yates shuffle.
        var natural = Enumerable.Range(0, _items.Count).ToList();
        for (var i = natural.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (natural[i], natural[j]) = (natural[j], natural[i]);
        }

        // Move the current item to the front if it is present.
        if (currentIndex >= 0)
        {
            var pos = natural.IndexOf(currentIndex);
            if (pos >= 0)
            {
                (natural[0], natural[pos]) = (natural[pos], natural[0]);
            }
        }

        _order.Clear();
        _order.AddRange(natural);
        _cursor = currentIndex >= 0 ? 0 : (_order.Count > 0 ? 0 : -1);
        IsShuffled = true;
    }

    /// <summary>Restores the natural (insertion) order, keeping the current item selected.</summary>
    public void Unshuffle()
    {
        var currentIndex = CurrentIndex;
        ResetOrder();
        if (currentIndex >= 0)
        {
            _cursor = _order.IndexOf(currentIndex);
        }

        IsShuffled = false;
    }

    /// <summary>Removes an item from the queue, keeping the cursor on a valid current item when possible.</summary>
    public bool Remove(T item)
    {
        var index = _items.IndexOf(item);
        if (index < 0)
        {
            return false;
        }

        // Capture the underlying item the cursor pointed at (in old items-index space) before mutating.
        var cursorTarget = _cursor >= 0 && _cursor < _order.Count ? _order[_cursor] : -1;
        var wasCurrent = cursorTarget == index;

        _items.RemoveAt(index);
        var newOrder = _order
            .Where(o => o != index)
            .Select(o => o > index ? o - 1 : o)
            .ToList();

        _order.Clear();
        _order.AddRange(newOrder);

        if (wasCurrent)
        {
            _cursor = _order.Count > 0 ? Math.Min(_cursor, _order.Count - 1) : -1;
        }
        else if (cursorTarget >= 0)
        {
            var shifted = cursorTarget > index ? cursorTarget - 1 : cursorTarget;
            _cursor = _order.IndexOf(shifted);
        }

        if (_order.Count == 0)
        {
            _cursor = -1;
        }

        return true;
    }

    /// <summary>Removes all items.</summary>
    public void Clear()
    {
        _items.Clear();
        _order.Clear();
        _cursor = -1;
        IsShuffled = false;
    }

    private void ResetOrder()
    {
        _order.Clear();
        for (var i = 0; i < _items.Count; i++)
        {
            _order.Add(i);
        }

        _cursor = -1;
        IsShuffled = false;
    }
}
