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
    public void Add_WhileNavigatedToOldest_EvictsAndMovesCursorToNewest()
    {
        var history = new SnapshotHistory(capacity: 2);
        history.Add(Make.Result("A"), Make.Timestamp(0), runNumber: 1);
        history.Add(Make.Result("B"), Make.Timestamp(1), runNumber: 2);
        history.MoveToOldest(); // cursor at A (index 0)

        history.Add(Make.Result("C"), Make.Timestamp(2), runNumber: 3);

        // A was evicted, B is now index 0, C is index 1.
        Assert.Equal(2, history.Count);
        Assert.Equal("B", history[0].Result.Output);
        Assert.Equal("C", history[1].Result.Output);
        // Add always moves cursor to newest.
        Assert.Equal(1, history.CursorIndex);
        Assert.Equal("C", history.Current.Result.Output);
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

// ===========================================================================
// Task 4: diff stats
// ===========================================================================
public class SnapshotHistory_DiffStatsTests
{
    [Fact]
    public void FirstSnapshot_LinesAddedEqualsLineCount_LinesRemovedIsZero()
    {
        var history = new SnapshotHistory(0);

        history.Add(Make.Result("line1\nline2\nline3"), Make.Timestamp(), 1);

        var snapshot = history[0];
        // SplitLines produces ["line1","line2","line3"] — 3 lines.
        Assert.Equal(3, snapshot.LinesAdded);
        Assert.Equal(0, snapshot.LinesRemoved);
    }

    [Fact]
    public void FirstSnapshot_EmptyOutput_ZeroStats()
    {
        var history = new SnapshotHistory(0);

        history.Add(Make.Result(""), Make.Timestamp(), 1);

        var snapshot = history[0];
        Assert.Equal(0, snapshot.LinesAdded);
        Assert.Equal(0, snapshot.LinesRemoved);
    }

    [Fact]
    public void IdenticalOutput_ZeroDiffStats()
    {
        var history = new SnapshotHistory(0);

        history.Add(Make.Result("line1\nline2"), Make.Timestamp(0), 1);
        history.Add(Make.Result("line1\nline2"), Make.Timestamp(1), 2);

        var snapshot = history[1];
        Assert.Equal(0, snapshot.LinesAdded);
        Assert.Equal(0, snapshot.LinesRemoved);
    }

    [Fact]
    public void ModifiedLine_CountsAsOneAddAndOneRemove()
    {
        var history = new SnapshotHistory(0);

        history.Add(Make.Result("unchanged\nold line"), Make.Timestamp(0), 1);
        history.Add(Make.Result("unchanged\nnew line"), Make.Timestamp(1), 2);

        var snapshot = history[1];
        Assert.Equal(1, snapshot.LinesAdded);
        Assert.Equal(1, snapshot.LinesRemoved);
    }

    [Fact]
    public void AddedLines_CountedCorrectly()
    {
        var history = new SnapshotHistory(0);

        history.Add(Make.Result("a\nb"),         Make.Timestamp(0), 1);
        history.Add(Make.Result("a\nb\nc\nd"),   Make.Timestamp(1), 2);

        var snapshot = history[1];
        Assert.Equal(2, snapshot.LinesAdded);
        Assert.Equal(0, snapshot.LinesRemoved);
    }

    [Fact]
    public void RemovedLines_CountedCorrectly()
    {
        var history = new SnapshotHistory(0);

        history.Add(Make.Result("a\nb\nc\nd"), Make.Timestamp(0), 1);
        history.Add(Make.Result("a\nb"),       Make.Timestamp(1), 2);

        var snapshot = history[1];
        Assert.Equal(0, snapshot.LinesAdded);
        Assert.Equal(2, snapshot.LinesRemoved);
    }

    [Fact]
    public void AnsiDifferencesIgnored_SameTextDifferentColours_ZeroStats()
    {
        const string plainOutput = "Build succeeded.\nWarnings: 0";

        // Same text with ANSI colour wrapping around "succeeded" and the number.
        string ansiOutput = "Build \x1b[32msucceeded.\x1b[0m\nWarnings: \x1b[33m0\x1b[0m";

        var history = new SnapshotHistory(0);

        history.Add(Make.Result(plainOutput), Make.Timestamp(0), 1);
        history.Add(Make.Result(ansiOutput),  Make.Timestamp(1), 2);

        var snapshot = history[1];
        Assert.Equal(0, snapshot.LinesAdded);
        Assert.Equal(0, snapshot.LinesRemoved);
    }

    [Fact]
    public void RunNumber_PreservedOnSnapshot()
    {
        var history = new SnapshotHistory(0);

        history.Add(Make.Result("x"), Make.Timestamp(), 7);

        Assert.Equal(7, history[0].RunNumber);
    }

    [Fact]
    public void Timestamp_PreservedOnSnapshot()
    {
        var history = new SnapshotHistory(0);
        var ts = new DateTime(2026, 6, 15, 12, 30, 45, DateTimeKind.Utc);

        history.Add(Make.Result("x"), ts, 1);

        Assert.Equal(ts, history[0].Timestamp);
    }

    [Fact]
    public void DuplicateLines_MultisetDiff_CountsExcessOnly()
    {
        // Previous has "a" twice, current has "a" three times — net +1 added.
        var history = new SnapshotHistory(0);

        history.Add(Make.Result("a\na"),       Make.Timestamp(0), 1);
        history.Add(Make.Result("a\na\na"),    Make.Timestamp(1), 2);

        var snapshot = history[1];
        Assert.Equal(1, snapshot.LinesAdded);
        Assert.Equal(0, snapshot.LinesRemoved);
    }

    [Fact]
    public void DiffStats_OutputEndingWithNewline_TrailingEmptyLineNotCounted()
    {
        var history = new SnapshotHistory(capacity: 10);

        // Output ending with \n should NOT count the trailing empty line.
        history.Add(Make.Result("line1\nline2\n"), DateTime.Now, runNumber: 1);

        Assert.Equal(2, history[0].LinesAdded); // 2, not 3
    }

    [Fact]
    public void DiffStats_NotAffectedByEviction()
    {
        // Capacity 2: after eviction the diff should still be against the
        // immediately preceding result, not some older entry.
        var history = new SnapshotHistory(2);

        history.Add(Make.Result("a\nb\nc"), Make.Timestamp(0), 1);
        history.Add(Make.Result("a\nb\nc"), Make.Timestamp(1), 2);
        // "first" gets evicted; diff is against "second" which is identical.
        history.Add(Make.Result("a\nb\nc"), Make.Timestamp(2), 3);

        var snapshot = history[1]; // newest after eviction
        Assert.Equal(0, snapshot.LinesAdded);
        Assert.Equal(0, snapshot.LinesRemoved);
    }
}
