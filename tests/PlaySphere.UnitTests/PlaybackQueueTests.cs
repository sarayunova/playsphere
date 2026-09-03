using PlaySphere.Domain.Playback;
using Xunit;

namespace PlaySphere.UnitTests;

public sealed class PlaybackQueueTests
{
    [Fact]
    public void Empty_queue_has_no_current_and_count_zero()
    {
        var queue = new PlaybackQueue<string>();

        Assert.True(queue.IsEmpty);
        Assert.Equal(0, queue.Count);
        Assert.Equal(-1, queue.Cursor);
        Assert.Null(queue.Current);
        Assert.Null(queue.MoveNext());
        Assert.Null(queue.MovePrevious());
    }

    [Fact]
    public void Start_selects_first_item_in_play_order()
    {
        var queue = new PlaybackQueue<string>(["a", "b", "c"]);

        queue.Start();

        Assert.Equal("a", queue.Current);
        Assert.Equal(0, queue.Cursor);
    }

    [Fact]
    public void MoveNext_in_off_mode_walks_to_end_then_returns_null()
    {
        var queue = new PlaybackQueue<string>(["a", "b", "c"]);
        queue.Start();

        Assert.Equal("b", queue.MoveNext());
        Assert.Equal("c", queue.MoveNext());
        Assert.Null(queue.MoveNext());
        Assert.Null(queue.Current);
    }

    [Fact]
    public void MoveNext_in_all_mode_wraps_from_end_to_start()
    {
        var queue = new PlaybackQueue<string>(["a", "b", "c"]) { RepeatMode = PlaybackRepeat.All };
        queue.Start();

        Assert.Equal("b", queue.MoveNext());
        Assert.Equal("c", queue.MoveNext());
        Assert.Equal("a", queue.MoveNext()); // wrapped
        Assert.Equal("b", queue.MoveNext());
    }

    [Fact]
    public void MoveNext_in_one_mode_repeats_the_current_item()
    {
        var queue = new PlaybackQueue<string>(["a", "b", "c"]) { RepeatMode = PlaybackRepeat.One };
        queue.Start();

        Assert.Equal("a", queue.MoveNext()); // still a
        Assert.Equal("a", queue.MoveNext());
        Assert.Equal("a", queue.Current);
    }

    [Fact]
    public void MoveNext_from_unstarted_starts_at_first_item()
    {
        var queue = new PlaybackQueue<string>(["a", "b", "c"]);

        Assert.Equal("a", queue.MoveNext());
    }

    [Fact]
    public void MovePrevious_wraps_from_first_to_last()
    {
        var queue = new PlaybackQueue<string>(["a", "b", "c"]);
        queue.Start(); // current = a

        Assert.Equal("c", queue.MovePrevious());
        Assert.Equal("b", queue.MovePrevious());
        Assert.Equal("a", queue.MovePrevious());
    }

    [Fact]
    public void MovePrevious_in_one_mode_keeps_current()
    {
        var queue = new PlaybackQueue<string>(["a", "b", "c"]) { RepeatMode = PlaybackRepeat.One };
        queue.Start();

        Assert.Equal("a", queue.MovePrevious());
        Assert.Equal("a", queue.Current);
    }

    [Fact]
    public void JumpTo_selects_the_requested_item()
    {
        var queue = new PlaybackQueue<string>(["a", "b", "c"]);

        Assert.True(queue.JumpTo("c"));
        Assert.Equal("c", queue.Current);
        Assert.True(queue.JumpTo("a"));
        Assert.Equal("a", queue.Current);
    }

    [Fact]
    public void JumpTo_returns_false_for_missing_item()
    {
        var queue = new PlaybackQueue<string>(["a", "b"]);

        Assert.False(queue.JumpTo("zzz"));
    }

    [Fact]
    public void Shuffle_keeps_current_at_front_and_permutes_the_rest()
    {
        var queue = new PlaybackQueue<string>(["a", "b", "c", "d"]);
        queue.JumpTo("c");

        queue.Shuffle(new Random(1234));

        Assert.True(queue.IsShuffled);
        Assert.Equal("c", queue.Current); // current stays at front
        Assert.Equal(0, queue.Cursor);
        Assert.Equal(4, queue.PlayOrder.Count);
        Assert.Equal(4, queue.PlayOrder.Distinct().Count()); // a full permutation, no duplicates
    }

    [Fact]
    public void Unshuffle_restores_natural_order_keeping_current()
    {
        var queue = new PlaybackQueue<string>(["a", "b", "c", "d"]);
        queue.JumpTo("b");
        queue.Shuffle(new Random(99));

        queue.Unshuffle();

        Assert.False(queue.IsShuffled);
        Assert.Equal(["a", "b", "c", "d"], queue.PlayOrder);
        Assert.Equal("b", queue.Current);
    }

    [Fact]
    public void Append_adds_item_and_participates_in_play_order()
    {
        var queue = new PlaybackQueue<string>(["a", "b"]);

        queue.Append("c");

        Assert.Equal(3, queue.Count);
        Assert.Equal(["a", "b", "c"], queue.PlayOrder);
    }

    [Fact]
    public void Remove_current_advances_cursor_to_a_valid_item()
    {
        var queue = new PlaybackQueue<string>(["a", "b", "c", "d"]);
        queue.JumpTo("b"); // current = b

        Assert.True(queue.Remove("b"));

        Assert.Equal(3, queue.Count);
        Assert.Equal("c", queue.Current); // shifted into b's slot
    }

    [Fact]
    public void Remove_non_current_keeps_the_current_item_selected()
    {
        var queue = new PlaybackQueue<string>(["a", "b", "c"]);
        queue.Start(); // current = a

        Assert.True(queue.Remove("c"));

        Assert.Equal("a", queue.Current);
        Assert.Equal(2, queue.Count);
    }

    [Fact]
    public void Remove_returns_false_for_missing_item()
    {
        var queue = new PlaybackQueue<string>(["a", "b"]);

        Assert.False(queue.Remove("zzz"));
    }

    [Fact]
    public void Remove_last_remaining_item_leaves_queue_empty()
    {
        var queue = new PlaybackQueue<string>(["a"]);
        queue.Start();

        Assert.True(queue.Remove("a"));

        Assert.True(queue.IsEmpty);
        Assert.Equal(-1, queue.Cursor);
        Assert.Null(queue.Current);
    }

    [Fact]
    public void Clear_removes_all_items()
    {
        var queue = new PlaybackQueue<string>(["a", "b", "c"]);
        queue.Start();

        queue.Clear();

        Assert.True(queue.IsEmpty);
        Assert.Equal(-1, queue.Cursor);
        Assert.Null(queue.Current);
    }

    [Fact]
    public void RepeatMode_all_and_shuffle_move_next_wraps_within_shuffled_order()
    {
        var queue = new PlaybackQueue<string>(["a", "b", "c"]) { RepeatMode = PlaybackRepeat.All };

        queue.Shuffle(new Random(7));
        var first = queue.Current;
        var seen = new HashSet<string> { first! };
        for (var i = 0; i < queue.Count - 1; i++)
        {
            seen.Add(queue.MoveNext()!);
        }

        Assert.Equal(3, seen.Count); // visited every item
        Assert.Equal(first, queue.MoveNext()); // wrapped back around
    }
}
