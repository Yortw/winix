using Xunit;
using Winix.Peep;

namespace Winix.Peep.Tests;

// ---------------------------------------------------------------------------
// Helpers shared across all test classes in this file
// ---------------------------------------------------------------------------
file static class Make
{
    private static readonly DateTime BaseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Creates a minimal <see cref="PeepResult"/> with the given output text.</summary>
    public static PeepResult Result(string output = "") =>
        new PeepResult(output, 0, TimeSpan.Zero, TriggerSource.Interval);

    /// <summary>Returns a deterministic timestamp offset by <paramref name="offsetSeconds"/>.</summary>
    public static DateTime Timestamp(int offsetSeconds = 0) =>
        BaseTime.AddSeconds(offsetSeconds);
}

// ===========================================================================
// Task 2: core add / retrieve / capacity
// ===========================================================================
public class SnapshotHistory_CoreTests
{
    [Fact]
    public void Add_SingleSnapshot_CountIsOne()
    {
        var history = new SnapshotHistory(0);

        history.Add(Make.Result("line1"), Make.Timestamp(), 1);

        Assert.Equal(1, history.Count);
    }

    [Fact]
    public void Add_MultipleSnapshots_CountGrows()
    {
        var history = new SnapshotHistory(0);

        history.Add(Make.Result("a"), Make.Timestamp(0), 1);
        history.Add(Make.Result("b"), Make.Timestamp(1), 2);
        history.Add(Make.Result("c"), Make.Timestamp(2), 3);

        Assert.Equal(3, history.Count);
    }

    [Fact]
    public void Indexer_ZeroIsOldest()
    {
        var history = new SnapshotHistory(0);

        history.Add(Make.Result("oldest"), Make.Timestamp(0), 1);
        history.Add(Make.Result("middle"), Make.Timestamp(1), 2);
        history.Add(Make.Result("newest"), Make.Timestamp(2), 3);

        Assert.Equal("oldest", history[0].Result.Output);
    }

    [Fact]
    public void Indexer_LastIsNewest()
    {
        var history = new SnapshotHistory(0);

        history.Add(Make.Result("oldest"), Make.Timestamp(0), 1);
        history.Add(Make.Result("newest"), Make.Timestamp(1), 2);

        Assert.Equal("newest", history[history.Count - 1].Result.Output);
    }

    [Fact]
    public void Capacity_Zero_UnlimitedRetention()
    {
        var history = new SnapshotHistory(0);

        for (int i = 1; i <= 100; i++)
        {
            history.Add(Make.Result($"run {i}"), Make.Timestamp(i), i);
        }

        Assert.Equal(100, history.Count);
    }

    [Fact]
    public void Capacity_Exceeded_EvictsOldest()
    {
        var history = new SnapshotHistory(3);

        history.Add(Make.Result("first"),  Make.Timestamp(0), 1);
        history.Add(Make.Result("second"), Make.Timestamp(1), 2);
        history.Add(Make.Result("third"),  Make.Timestamp(2), 3);
        // This should evict "first"
        history.Add(Make.Result("fourth"), Make.Timestamp(3), 4);

        Assert.Equal(3, history.Count);
        Assert.Equal("second", history[0].Result.Output);
        Assert.Equal("fourth", history[2].Result.Output);
    }

    [Fact]
    public void Capacity_ExactlyAtLimit_DoesNotEvict()
    {
        var history = new SnapshotHistory(3);

        history.Add(Make.Result("a"), Make.Timestamp(0), 1);
        history.Add(Make.Result("b"), Make.Timestamp(1), 2);
        history.Add(Make.Result("c"), Make.Timestamp(2), 3);

        Assert.Equal(3, history.Count);
        Assert.Equal("a", history[0].Result.Output);
    }

    [Fact]
    public void Capacity_Property_ReturnsConstructorValue()
    {
        var history = new SnapshotHistory(42);

        Assert.Equal(42, history.Capacity);
    }

    [Fact]
    public void Capacity_Negative_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SnapshotHistory(-1));
    }

    [Fact]
    public void Add_MovesCurrentToNewest()
    {
        var history = new SnapshotHistory(0);

        history.Add(Make.Result("first"),  Make.Timestamp(0), 1);
        history.Add(Make.Result("second"), Make.Timestamp(1), 2);

        Assert.Equal("second", history.Current.Result.Output);
    }
}

// ===========================================================================
// Task 3: cursor navigation
// ===========================================================================
public class SnapshotHistory_CursorTests
{
    [Fact]
    public void Current_EmptyHistory_Throws()
    {
        var history = new SnapshotHistory(0);

        Assert.Throws<InvalidOperationException>(() => _ = history.Current);
    }

    [Fact]
    public void Current_AfterAdd_PointsToNewest()
    {
        var history = new SnapshotHistory(0);

        history.Add(Make.Result("a"), Make.Timestamp(0), 1);
        history.Add(Make.Result("b"), Make.Timestamp(1), 2);

        Assert.Equal("b", history.Current.Result.Output);
    }

    [Fact]
    public void CursorIndex_EmptyHistory_IsMinusOne()
    {
        var history = new SnapshotHistory(0);

        Assert.Equal(-1, history.CursorIndex);
    }

    [Fact]
    public void CursorIndex_AfterFirstAdd_IsZero()
    {
        var history = new SnapshotHistory(0);

        history.Add(Make.Result("a"), Make.Timestamp(0), 1);

        Assert.Equal(0, history.CursorIndex);
    }

    [Fact]
    public void IsAtNewest_EmptyHistory_ReturnsTrue()
    {
        var history = new SnapshotHistory(0);

        Assert.True(history.IsAtNewest);
    }

    [Fact]
    public void IsAtNewest_CursorAtNewest_ReturnsTrue()
    {
        var history = new SnapshotHistory(0);

        history.Add(Make.Result("a"), Make.Timestamp(0), 1);
        history.Add(Make.Result("b"), Make.Timestamp(1), 2);

        Assert.True(history.IsAtNewest);
    }

    [Fact]
    public void IsAtNewest_CursorMovedOlder_ReturnsFalse()
    {
        var history = new SnapshotHistory(0);

        history.Add(Make.Result("a"), Make.Timestamp(0), 1);
        history.Add(Make.Result("b"), Make.Timestamp(1), 2);
        history.MoveOlder();

        Assert.False(history.IsAtNewest);
    }

    [Fact]
    public void MoveOlder_FromNewest_ReturnsTrueAndMovesCursor()
    {
        var history = new SnapshotHistory(0);

        history.Add(Make.Result("a"), Make.Timestamp(0), 1);
        history.Add(Make.Result("b"), Make.Timestamp(1), 2);

        bool moved = history.MoveOlder();

        Assert.True(moved);
        Assert.Equal("a", history.Current.Result.Output);
    }

    [Fact]
    public void MoveOlder_AtOldest_ReturnsFalse()
    {
        var history = new SnapshotHistory(0);

        history.Add(Make.Result("a"), Make.Timestamp(0), 1);

        bool moved = history.MoveOlder();

        Assert.False(moved);
        Assert.Equal("a", history.Current.Result.Output);
    }

    [Fact]
    public void MoveOlder_EmptyHistory_ReturnsFalse()
    {
        var history = new SnapshotHistory(0);

        Assert.False(history.MoveOlder());
    }

    [Fact]
    public void MoveNewer_FromOldest_ReturnsTrueAndMovesCursor()
    {
        var history = new SnapshotHistory(0);

        history.Add(Make.Result("a"), Make.Timestamp(0), 1);
        history.Add(Make.Result("b"), Make.Timestamp(1), 2);
        history.MoveOlder();

        bool moved = history.MoveNewer();

        Assert.True(moved);
        Assert.Equal("b", history.Current.Result.Output);
    }

    [Fact]
    public void MoveNewer_AtNewest_ReturnsFalse()
    {
        var history = new SnapshotHistory(0);

        history.Add(Make.Result("a"), Make.Timestamp(0), 1);

        bool moved = history.MoveNewer();

        Assert.False(moved);
    }

    [Fact]
    public void MoveNewer_EmptyHistory_ReturnsFalse()
    {
        var history = new SnapshotHistory(0);

        Assert.False(history.MoveNewer());
    }

    [Fact]
    public void MoveToOldest_MovesCursorToIndexZero()
    {
        var history = new SnapshotHistory(0);

        history.Add(Make.Result("a"), Make.Timestamp(0), 1);
        history.Add(Make.Result("b"), Make.Timestamp(1), 2);
        history.Add(Make.Result("c"), Make.Timestamp(2), 3);

        history.MoveToOldest();

        Assert.Equal(0, history.CursorIndex);
        Assert.Equal("a", history.Current.Result.Output);
    }

    [Fact]
    public void MoveToOldest_EmptyHistory_IsNoOp()
    {
        var history = new SnapshotHistory(0);

        history.MoveToOldest(); // should not throw

        Assert.Equal(-1, history.CursorIndex);
    }

    [Fact]
    public void MoveToNewest_MovesCursorToLastIndex()
    {
        var history = new SnapshotHistory(0);

        history.Add(Make.Result("a"), Make.Timestamp(0), 1);
        history.Add(Make.Result("b"), Make.Timestamp(1), 2);
        history.Add(Make.Result("c"), Make.Timestamp(2), 3);
        history.MoveToOldest();

        history.MoveToNewest();

        Assert.Equal(2, history.CursorIndex);
        Assert.Equal("c", history.Current.Result.Output);
    }

    [Fact]
    public void MoveToNewest_EmptyHistory_IsNoOp()
    {
        var history = new SnapshotHistory(0);

        history.MoveToNewest(); // should not throw

        Assert.Equal(-1, history.CursorIndex);
    }

    [Fact]
    public void GetPreviousOf_MiddleIndex_ReturnsPreviousSnapshot()
    {
        var history = new SnapshotHistory(0);

        history.Add(Make.Result("a"), Make.Timestamp(0), 1);
        history.Add(Make.Result("b"), Make.Timestamp(1), 2);
        history.Add(Make.Result("c"), Make.Timestamp(2), 3);

        var previous = history.GetPreviousOf(2);

        Assert.NotNull(previous);
        Assert.Equal("b", previous.Result.Output);
    }

    [Fact]
    public void GetPreviousOf_IndexZero_ReturnsNull()
    {
        var history = new SnapshotHistory(0);

        history.Add(Make.Result("a"), Make.Timestamp(0), 1);

        Assert.Null(history.GetPreviousOf(0));
    }

    [Fact]
    public void GetPreviousOf_OutOfRange_ReturnsNull()
    {
        var history = new SnapshotHistory(0);

        history.Add(Make.Result("a"), Make.Timestamp(0), 1);

        Assert.Null(history.GetPreviousOf(99));
    }

    [Fact]
    public void Add_AfterNavigation_MovesCursorBackToNewest()
    {
        var history = new SnapshotHistory(0);

        history.Add(Make.Result("a"), Make.Timestamp(0), 1);
        history.Add(Make.Result("b"), Make.Timestamp(1), 2);
        history.MoveToOldest();

        // Adding a new snapshot should bring the cursor back to the end.
        history.Add(Make.Result("c"), Make.Timestamp(2), 3);

        Assert.True(history.IsAtNewest);
        Assert.Equal("c", history.Current.Result.Output);
    }
}
