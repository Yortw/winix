# Peep Time Machine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a time machine feature to peep that lets users browse back through historical command output snapshots during a session, using Left/Right arrow step navigation and a `t`-key history overlay.

**Architecture:** New `Snapshot` record and `SnapshotHistory` class in the Winix.Peep library hold an in-memory bounded ring buffer of past results with cursor navigation. The existing polling event loop in Program.cs gains a time machine mode (auto-pauses, Left/Right steps, `t` overlay). ScreenRenderer gets a new history overlay method and updated header formatting. All new logic is TDD with xUnit.

**Tech Stack:** .NET 10, C#, xUnit, AOT-compatible (no reflection)

---

## File Map

| File | Action | Responsibility |
|------|--------|---------------|
| `src/Winix.Peep/Snapshot.cs` | Create | Immutable record wrapping PeepResult + timestamp + run number + diff stats |
| `src/Winix.Peep/SnapshotHistory.cs` | Create | Bounded list with cursor navigation (add, move, index, capacity) |
| `src/Winix.Peep/ScreenRenderer.cs` | Modify | Add `RenderHistoryOverlay`, add `FormatTimeMachineHeader`, update help text lines |
| `src/Winix.Peep/Formatting.cs` | Modify | Add `history_retained` field to `FormatJson` |
| `src/peep/Program.cs` | Modify | Add `--history` arg, time machine state, key handling, overlay state, history overlay selection |
| `tests/Winix.Peep.Tests/SnapshotHistoryTests.cs` | Create | Unit tests for SnapshotHistory |
| `tests/Winix.Peep.Tests/ScreenRendererTests.cs` | Modify | Tests for history overlay and time machine header |
| `tests/Winix.Peep.Tests/FormattingTests.cs` | Modify | Test `history_retained` field in JSON output |

---

### Task 1: Snapshot record

**Files:**
- Create: `src/Winix.Peep/Snapshot.cs`

- [ ] **Step 1: Create Snapshot record**

```csharp
namespace Winix.Peep;

/// <summary>
/// Immutable snapshot of a single command execution, stored in <see cref="SnapshotHistory"/>
/// for time machine browsing.
/// </summary>
/// <param name="Result">The command execution result.</param>
/// <param name="Timestamp">Wall clock time when the run started.</param>
/// <param name="RunNumber">1-based sequential run number within the session.</param>
/// <param name="LinesAdded">Lines added compared to the previous snapshot (0 for the first snapshot's removals).</param>
/// <param name="LinesRemoved">Lines removed compared to the previous snapshot (0 for the first snapshot).</param>
public sealed record Snapshot(
    PeepResult Result,
    DateTime Timestamp,
    int RunNumber,
    int LinesAdded,
    int LinesRemoved
);
```

- [ ] **Step 2: Verify it builds**

Run: `dotnet build src/Winix.Peep/Winix.Peep.csproj`
Expected: Build succeeded, 0 warnings

- [ ] **Step 3: Commit**

```
git add src/Winix.Peep/Snapshot.cs
git commit -m "feat: add Snapshot record for peep time machine history"
```

---

### Task 2: SnapshotHistory — core add/retrieve/capacity

**Files:**
- Create: `src/Winix.Peep/SnapshotHistory.cs`
- Create: `tests/Winix.Peep.Tests/SnapshotHistoryTests.cs`

- [ ] **Step 1: Write failing tests for Add, Count, indexer, and Capacity**

In `tests/Winix.Peep.Tests/SnapshotHistoryTests.cs`:

```csharp
using Xunit;
using Winix.Peep;

namespace Winix.Peep.Tests;

public class SnapshotHistoryTests
{
    private static PeepResult MakeResult(string output = "output", int exitCode = 0)
    {
        return new PeepResult(output, exitCode, TimeSpan.FromSeconds(1), TriggerSource.Interval);
    }

    [Fact]
    public void Add_SingleSnapshot_CountIsOne()
    {
        var history = new SnapshotHistory(capacity: 10);

        history.Add(MakeResult(), new DateTime(2026, 3, 30, 14, 0, 0), runNumber: 1);

        Assert.Equal(1, history.Count);
    }

    [Fact]
    public void Add_MultipleSnapshots_CountMatches()
    {
        var history = new SnapshotHistory(capacity: 10);

        history.Add(MakeResult("a"), new DateTime(2026, 3, 30, 14, 0, 0), runNumber: 1);
        history.Add(MakeResult("b"), new DateTime(2026, 3, 30, 14, 0, 2), runNumber: 2);
        history.Add(MakeResult("c"), new DateTime(2026, 3, 30, 14, 0, 4), runNumber: 3);

        Assert.Equal(3, history.Count);
    }

    [Fact]
    public void Indexer_ZeroIsOldest_LastIsNewest()
    {
        var history = new SnapshotHistory(capacity: 10);

        history.Add(MakeResult("first"), new DateTime(2026, 3, 30, 14, 0, 0), runNumber: 1);
        history.Add(MakeResult("second"), new DateTime(2026, 3, 30, 14, 0, 2), runNumber: 2);

        Assert.Equal("first", history[0].Result.Output);
        Assert.Equal("second", history[1].Result.Output);
    }

    [Fact]
    public void Capacity_DropsOldestWhenExceeded()
    {
        var history = new SnapshotHistory(capacity: 2);

        history.Add(MakeResult("a"), new DateTime(2026, 3, 30, 14, 0, 0), runNumber: 1);
        history.Add(MakeResult("b"), new DateTime(2026, 3, 30, 14, 0, 2), runNumber: 2);
        history.Add(MakeResult("c"), new DateTime(2026, 3, 30, 14, 0, 4), runNumber: 3);

        Assert.Equal(2, history.Count);
        Assert.Equal("b", history[0].Result.Output);
        Assert.Equal("c", history[1].Result.Output);
    }

    [Fact]
    public void Capacity_ZeroMeansUnlimited()
    {
        var history = new SnapshotHistory(capacity: 0);

        for (int i = 0; i < 100; i++)
        {
            history.Add(MakeResult($"run{i}"), DateTime.Now, runNumber: i + 1);
        }

        Assert.Equal(100, history.Count);
    }

    [Fact]
    public void Capacity_PropertyReturnsConfiguredValue()
    {
        var history = new SnapshotHistory(capacity: 500);

        Assert.Equal(500, history.Capacity);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Winix.Peep.Tests/ --filter "SnapshotHistoryTests"`
Expected: FAIL — `SnapshotHistory` does not exist

- [ ] **Step 3: Implement SnapshotHistory with Add, Count, indexer, Capacity**

In `src/Winix.Peep/SnapshotHistory.cs`:

```csharp
namespace Winix.Peep;

/// <summary>
/// Bounded in-memory collection of <see cref="Snapshot"/> entries with cursor navigation
/// for peep's time machine feature. Oldest entries are evicted when capacity is exceeded.
/// </summary>
public sealed class SnapshotHistory
{
    private readonly List<Snapshot> _snapshots = new();
    private int _cursorIndex = -1;

    /// <summary>
    /// Creates a new snapshot history with the specified maximum capacity.
    /// </summary>
    /// <param name="capacity">Maximum snapshots to retain. 0 means unlimited.</param>
    public SnapshotHistory(int capacity)
    {
        Capacity = capacity;
    }

    /// <summary>Maximum number of snapshots retained. 0 means unlimited.</summary>
    public int Capacity { get; }

    /// <summary>Number of snapshots currently stored.</summary>
    public int Count => _snapshots.Count;

    /// <summary>
    /// Retrieves a snapshot by index. Index 0 is the oldest, <see cref="Count"/>-1 is the newest.
    /// </summary>
    public Snapshot this[int index] => _snapshots[index];

    /// <summary>
    /// Appends a new snapshot, computing diff stats against the previous entry.
    /// Evicts the oldest snapshot if capacity is exceeded. Moves cursor to newest.
    /// </summary>
    /// <param name="result">The command execution result.</param>
    /// <param name="timestamp">Wall clock time when the run started.</param>
    /// <param name="runNumber">1-based sequential run number.</param>
    public void Add(PeepResult result, DateTime timestamp, int runNumber)
    {
        // Compute diff stats against previous snapshot
        int linesAdded = 0;
        int linesRemoved = 0;

        string[] currentLines = result.Output.Split('\n');

        if (_snapshots.Count > 0)
        {
            Snapshot previous = _snapshots[^1];
            string[] previousLines = previous.Result.Output.Split('\n');
            string[] currentStripped = Array.ConvertAll(currentLines, Formatting.StripAnsi);
            string[] previousStripped = Array.ConvertAll(previousLines, Formatting.StripAnsi);

            int maxLen = Math.Max(currentStripped.Length, previousStripped.Length);
            for (int i = 0; i < maxLen; i++)
            {
                string cur = i < currentStripped.Length ? currentStripped[i] : "";
                string prev = i < previousStripped.Length ? previousStripped[i] : "";
                if (!string.Equals(cur, prev, StringComparison.Ordinal))
                {
                    if (i >= previousStripped.Length)
                    {
                        linesAdded++;
                    }
                    else if (i >= currentStripped.Length)
                    {
                        linesRemoved++;
                    }
                    else
                    {
                        // Line changed — count as both an add and a remove
                        linesAdded++;
                        linesRemoved++;
                    }
                }
            }
        }
        else
        {
            // First snapshot: all lines are "added"
            linesAdded = currentLines.Length;
        }

        var snapshot = new Snapshot(result, timestamp, runNumber, linesAdded, linesRemoved);
        _snapshots.Add(snapshot);

        // Evict oldest if over capacity (0 = unlimited)
        if (Capacity > 0 && _snapshots.Count > Capacity)
        {
            _snapshots.RemoveAt(0);
        }

        // Move cursor to newest
        _cursorIndex = _snapshots.Count - 1;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Winix.Peep.Tests/ --filter "SnapshotHistoryTests"`
Expected: All 6 tests PASS

- [ ] **Step 5: Commit**

```
git add src/Winix.Peep/SnapshotHistory.cs tests/Winix.Peep.Tests/SnapshotHistoryTests.cs
git commit -m "feat: add SnapshotHistory with add, indexer, and capacity enforcement"
```

---

### Task 3: SnapshotHistory — cursor navigation

**Files:**
- Modify: `src/Winix.Peep/SnapshotHistory.cs`
- Modify: `tests/Winix.Peep.Tests/SnapshotHistoryTests.cs`

- [ ] **Step 1: Write failing tests for cursor navigation**

Append to `SnapshotHistoryTests.cs`:

```csharp
    [Fact]
    public void Current_AfterAdd_ReturnsNewest()
    {
        var history = new SnapshotHistory(capacity: 10);
        history.Add(MakeResult("a"), DateTime.Now, runNumber: 1);
        history.Add(MakeResult("b"), DateTime.Now, runNumber: 2);

        Assert.Equal("b", history.Current.Result.Output);
    }

    [Fact]
    public void CursorIndex_AfterAdd_IsAtNewest()
    {
        var history = new SnapshotHistory(capacity: 10);
        history.Add(MakeResult("a"), DateTime.Now, runNumber: 1);
        history.Add(MakeResult("b"), DateTime.Now, runNumber: 2);

        Assert.Equal(1, history.CursorIndex);
    }

    [Fact]
    public void IsAtNewest_AfterAdd_True()
    {
        var history = new SnapshotHistory(capacity: 10);
        history.Add(MakeResult("a"), DateTime.Now, runNumber: 1);

        Assert.True(history.IsAtNewest);
    }

    [Fact]
    public void MoveOlder_StepsBack_ReturnsTrueWhenSuccessful()
    {
        var history = new SnapshotHistory(capacity: 10);
        history.Add(MakeResult("a"), DateTime.Now, runNumber: 1);
        history.Add(MakeResult("b"), DateTime.Now, runNumber: 2);

        bool moved = history.MoveOlder();

        Assert.True(moved);
        Assert.Equal(0, history.CursorIndex);
        Assert.Equal("a", history.Current.Result.Output);
    }

    [Fact]
    public void MoveOlder_AtOldest_ReturnsFalse()
    {
        var history = new SnapshotHistory(capacity: 10);
        history.Add(MakeResult("a"), DateTime.Now, runNumber: 1);
        history.Add(MakeResult("b"), DateTime.Now, runNumber: 2);

        history.MoveOlder(); // now at index 0
        bool moved = history.MoveOlder();

        Assert.False(moved);
        Assert.Equal(0, history.CursorIndex);
    }

    [Fact]
    public void MoveNewer_StepsForward_ReturnsTrueWhenSuccessful()
    {
        var history = new SnapshotHistory(capacity: 10);
        history.Add(MakeResult("a"), DateTime.Now, runNumber: 1);
        history.Add(MakeResult("b"), DateTime.Now, runNumber: 2);

        history.MoveOlder();
        bool moved = history.MoveNewer();

        Assert.True(moved);
        Assert.Equal(1, history.CursorIndex);
        Assert.True(history.IsAtNewest);
    }

    [Fact]
    public void MoveNewer_AtNewest_ReturnsFalse()
    {
        var history = new SnapshotHistory(capacity: 10);
        history.Add(MakeResult("a"), DateTime.Now, runNumber: 1);

        bool moved = history.MoveNewer();

        Assert.False(moved);
    }

    [Fact]
    public void MoveToOldest_SetsCursorToZero()
    {
        var history = new SnapshotHistory(capacity: 10);
        history.Add(MakeResult("a"), DateTime.Now, runNumber: 1);
        history.Add(MakeResult("b"), DateTime.Now, runNumber: 2);
        history.Add(MakeResult("c"), DateTime.Now, runNumber: 3);

        history.MoveToOldest();

        Assert.Equal(0, history.CursorIndex);
        Assert.Equal("a", history.Current.Result.Output);
    }

    [Fact]
    public void MoveToNewest_SetsCursorToLast()
    {
        var history = new SnapshotHistory(capacity: 10);
        history.Add(MakeResult("a"), DateTime.Now, runNumber: 1);
        history.Add(MakeResult("b"), DateTime.Now, runNumber: 2);
        history.Add(MakeResult("c"), DateTime.Now, runNumber: 3);

        history.MoveToOldest();
        history.MoveToNewest();

        Assert.Equal(2, history.CursorIndex);
        Assert.True(history.IsAtNewest);
    }

    [Fact]
    public void GetPreviousOf_ReturnsSnapshotBeforeIndex()
    {
        var history = new SnapshotHistory(capacity: 10);
        history.Add(MakeResult("a"), DateTime.Now, runNumber: 1);
        history.Add(MakeResult("b"), DateTime.Now, runNumber: 2);

        Snapshot? prev = history.GetPreviousOf(1);

        Assert.NotNull(prev);
        Assert.Equal("a", prev.Result.Output);
    }

    [Fact]
    public void GetPreviousOf_AtZero_ReturnsNull()
    {
        var history = new SnapshotHistory(capacity: 10);
        history.Add(MakeResult("a"), DateTime.Now, runNumber: 1);

        Snapshot? prev = history.GetPreviousOf(0);

        Assert.Null(prev);
    }

    [Fact]
    public void Empty_Current_ThrowsInvalidOperation()
    {
        var history = new SnapshotHistory(capacity: 10);

        Assert.Throws<InvalidOperationException>(() => history.Current);
    }

    [Fact]
    public void Empty_MoveOlder_ReturnsFalse()
    {
        var history = new SnapshotHistory(capacity: 10);

        Assert.False(history.MoveOlder());
    }

    [Fact]
    public void Empty_MoveNewer_ReturnsFalse()
    {
        var history = new SnapshotHistory(capacity: 10);

        Assert.False(history.MoveNewer());
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Winix.Peep.Tests/ --filter "SnapshotHistoryTests"`
Expected: FAIL — `Current`, `CursorIndex`, `IsAtNewest`, `MoveOlder`, etc. do not exist

- [ ] **Step 3: Implement cursor navigation methods**

Add to the `SnapshotHistory` class body in `src/Winix.Peep/SnapshotHistory.cs`:

```csharp
    /// <summary>Returns the snapshot at the current cursor position.</summary>
    /// <exception cref="InvalidOperationException">History is empty.</exception>
    public Snapshot Current
    {
        get
        {
            if (_snapshots.Count == 0)
            {
                throw new InvalidOperationException("Snapshot history is empty.");
            }

            return _snapshots[_cursorIndex];
        }
    }

    /// <summary>Current cursor position (0 = oldest).</summary>
    public int CursorIndex => _cursorIndex;

    /// <summary>Whether the cursor is at the newest snapshot.</summary>
    public bool IsAtNewest => _snapshots.Count > 0 && _cursorIndex == _snapshots.Count - 1;

    /// <summary>Moves cursor one step towards older. Returns false if already at oldest or empty.</summary>
    public bool MoveOlder()
    {
        if (_cursorIndex <= 0)
        {
            return false;
        }

        _cursorIndex--;
        return true;
    }

    /// <summary>Moves cursor one step towards newer. Returns false if already at newest or empty.</summary>
    public bool MoveNewer()
    {
        if (_cursorIndex >= _snapshots.Count - 1)
        {
            return false;
        }

        _cursorIndex++;
        return true;
    }

    /// <summary>Moves cursor to the oldest snapshot.</summary>
    public void MoveToOldest()
    {
        if (_snapshots.Count > 0)
        {
            _cursorIndex = 0;
        }
    }

    /// <summary>Moves cursor to the newest snapshot.</summary>
    public void MoveToNewest()
    {
        if (_snapshots.Count > 0)
        {
            _cursorIndex = _snapshots.Count - 1;
        }
    }

    /// <summary>Returns the snapshot before the one at <paramref name="index"/>, or null if index is 0.</summary>
    public Snapshot? GetPreviousOf(int index)
    {
        if (index <= 0 || index >= _snapshots.Count)
        {
            return null;
        }

        return _snapshots[index - 1];
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Winix.Peep.Tests/ --filter "SnapshotHistoryTests"`
Expected: All tests PASS

- [ ] **Step 5: Commit**

```
git add src/Winix.Peep/SnapshotHistory.cs tests/Winix.Peep.Tests/SnapshotHistoryTests.cs
git commit -m "feat: add cursor navigation to SnapshotHistory"
```

---

### Task 4: SnapshotHistory — diff stats computation

**Files:**
- Modify: `tests/Winix.Peep.Tests/SnapshotHistoryTests.cs`

- [ ] **Step 1: Write failing tests for diff stats**

Append to `SnapshotHistoryTests.cs`:

```csharp
    [Fact]
    public void DiffStats_FirstSnapshot_AllLinesAdded()
    {
        var history = new SnapshotHistory(capacity: 10);

        history.Add(MakeResult("line1\nline2\nline3"), DateTime.Now, runNumber: 1);

        Assert.Equal(3, history[0].LinesAdded);
        Assert.Equal(0, history[0].LinesRemoved);
    }

    [Fact]
    public void DiffStats_IdenticalOutput_ZeroChanges()
    {
        var history = new SnapshotHistory(capacity: 10);

        history.Add(MakeResult("same"), DateTime.Now, runNumber: 1);
        history.Add(MakeResult("same"), DateTime.Now, runNumber: 2);

        Assert.Equal(0, history[1].LinesAdded);
        Assert.Equal(0, history[1].LinesRemoved);
    }

    [Fact]
    public void DiffStats_ModifiedLine_CountsAsAddAndRemove()
    {
        var history = new SnapshotHistory(capacity: 10);

        history.Add(MakeResult("line1\noriginal\nline3"), DateTime.Now, runNumber: 1);
        history.Add(MakeResult("line1\nchanged\nline3"), DateTime.Now, runNumber: 2);

        Assert.Equal(1, history[1].LinesAdded);
        Assert.Equal(1, history[1].LinesRemoved);
    }

    [Fact]
    public void DiffStats_AddedLines_CountedCorrectly()
    {
        var history = new SnapshotHistory(capacity: 10);

        history.Add(MakeResult("line1"), DateTime.Now, runNumber: 1);
        history.Add(MakeResult("line1\nline2\nline3"), DateTime.Now, runNumber: 2);

        Assert.Equal(2, history[1].LinesAdded);
        Assert.Equal(0, history[1].LinesRemoved);
    }

    [Fact]
    public void DiffStats_RemovedLines_CountedCorrectly()
    {
        var history = new SnapshotHistory(capacity: 10);

        history.Add(MakeResult("line1\nline2\nline3"), DateTime.Now, runNumber: 1);
        history.Add(MakeResult("line1"), DateTime.Now, runNumber: 2);

        Assert.Equal(0, history[1].LinesAdded);
        Assert.Equal(2, history[1].LinesRemoved);
    }

    [Fact]
    public void DiffStats_IgnoresAnsiDifferences()
    {
        var history = new SnapshotHistory(capacity: 10);

        history.Add(MakeResult("\x1b[32mgreen\x1b[0m"), DateTime.Now, runNumber: 1);
        history.Add(MakeResult("\x1b[31mgreen\x1b[0m"), DateTime.Now, runNumber: 2);

        // Same text, different ANSI — should count as zero changes
        Assert.Equal(0, history[1].LinesAdded);
        Assert.Equal(0, history[1].LinesRemoved);
    }

    [Fact]
    public void DiffStats_RunNumberPreserved()
    {
        var history = new SnapshotHistory(capacity: 10);

        history.Add(MakeResult("a"), DateTime.Now, runNumber: 42);

        Assert.Equal(42, history[0].RunNumber);
    }

    [Fact]
    public void DiffStats_TimestampPreserved()
    {
        var history = new SnapshotHistory(capacity: 10);
        var ts = new DateTime(2026, 3, 30, 14, 0, 0);

        history.Add(MakeResult("a"), ts, runNumber: 1);

        Assert.Equal(ts, history[0].Timestamp);
    }
```

- [ ] **Step 2: Run tests to verify they pass (diff stats were implemented in Task 2)**

Run: `dotnet test tests/Winix.Peep.Tests/ --filter "SnapshotHistoryTests"`
Expected: All tests PASS (diff stats logic was included in the `Add` implementation)

- [ ] **Step 3: Commit**

```
git add tests/Winix.Peep.Tests/SnapshotHistoryTests.cs
git commit -m "test: add diff stats and metadata tests for SnapshotHistory"
```

---

### Task 5: ScreenRenderer — time machine header format

**Files:**
- Modify: `src/Winix.Peep/ScreenRenderer.cs`
- Modify: `tests/Winix.Peep.Tests/ScreenRendererTests.cs`

- [ ] **Step 1: Write failing tests for time machine header**

Append new test class to `tests/Winix.Peep.Tests/ScreenRendererTests.cs`:

```csharp
public class TimeMachineHeaderTests
{
    [Fact]
    public void FormatHeader_WithTimeMachine_ShowsTimeIndicator()
    {
        string header = ScreenRenderer.FormatHeader(
            intervalSeconds: 2.0,
            command: "dotnet build",
            timestamp: new DateTime(2026, 3, 30, 14, 32, 5),
            exitCode: 0,
            runCount: 3,
            isPaused: true,
            useColor: false,
            isDiffEnabled: false,
            isTimeMachine: true,
            timeMachinePosition: 3,
            timeMachineTotal: 17);

        Assert.Contains("[TIME", header);
        Assert.Contains("3/17", header);
    }

    [Fact]
    public void FormatHeader_WithTimeMachine_ShowsSnapshotRunCount()
    {
        string header = ScreenRenderer.FormatHeader(
            intervalSeconds: 2.0,
            command: "dotnet build",
            timestamp: new DateTime(2026, 3, 30, 14, 32, 5),
            exitCode: 0,
            runCount: 3,
            isPaused: true,
            useColor: false,
            isTimeMachine: true,
            timeMachinePosition: 5,
            timeMachineTotal: 10);

        // Run count shows snapshot position, not live count
        Assert.Contains("[run #3]", header);
        Assert.Contains("[TIME 5/10]", header);
    }

    [Fact]
    public void FormatHeader_NoTimeMachine_NoTimeIndicator()
    {
        string header = ScreenRenderer.FormatHeader(
            intervalSeconds: 2.0,
            command: "dotnet build",
            timestamp: DateTime.Now,
            exitCode: 0,
            runCount: 1,
            isPaused: false,
            useColor: false);

        Assert.DoesNotContain("[TIME", header);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Winix.Peep.Tests/ --filter "TimeMachineHeaderTests"`
Expected: FAIL — `FormatHeader` does not accept `isTimeMachine`/`timeMachinePosition`/`timeMachineTotal` parameters

- [ ] **Step 3: Add time machine parameters to FormatHeader**

In `src/Winix.Peep/ScreenRenderer.cs`, update the `FormatHeader` signature and body. Add three new optional parameters after `isDiffEnabled`:

```csharp
    public static string FormatHeader(
        double intervalSeconds,
        string command,
        DateTime timestamp,
        int? exitCode,
        int runCount,
        bool isPaused,
        bool useColor,
        bool isDiffEnabled = false,
        bool isTimeMachine = false,
        int timeMachinePosition = 0,
        int timeMachineTotal = 0)
```

Add after the `[DIFF]` append block:

```csharp
        if (isTimeMachine)
        {
            sb.AppendFormat(CultureInfo.InvariantCulture,
                " [TIME {0}/{1}]", timeMachinePosition, timeMachineTotal);
        }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Winix.Peep.Tests/ --filter "FullyQualifiedName~ScreenRenderer|FullyQualifiedName~FormatHeader|FullyQualifiedName~TimeMachine"`
Expected: All PASS (including existing header tests — new params have defaults)

- [ ] **Step 5: Commit**

```
git add src/Winix.Peep/ScreenRenderer.cs tests/Winix.Peep.Tests/ScreenRendererTests.cs
git commit -m "feat: add time machine indicator to peep header"
```

---

### Task 6: ScreenRenderer — history overlay

**Files:**
- Modify: `src/Winix.Peep/ScreenRenderer.cs`
- Modify: `tests/Winix.Peep.Tests/ScreenRendererTests.cs`

- [ ] **Step 1: Write failing tests for RenderHistoryOverlay**

Append new test class to `tests/Winix.Peep.Tests/ScreenRendererTests.cs`:

```csharp
public class HistoryOverlayTests
{
    private static PeepResult MakeResult(string output = "output", int exitCode = 0)
    {
        return new PeepResult(output, exitCode, TimeSpan.FromSeconds(1), TriggerSource.Interval);
    }

    [Fact]
    public void RenderHistoryOverlay_ShowsRunNumbers()
    {
        var history = new SnapshotHistory(capacity: 10);
        history.Add(MakeResult("a"), new DateTime(2026, 3, 30, 14, 0, 0), runNumber: 1);
        history.Add(MakeResult("b"), new DateTime(2026, 3, 30, 14, 0, 2), runNumber: 2);

        var writer = new StringWriter();
        ScreenRenderer.RenderHistoryOverlay(writer, history, selectedIndex: 1, width: 80, height: 24);

        string output = writer.ToString();
        Assert.Contains("#1", output);
        Assert.Contains("#2", output);
    }

    [Fact]
    public void RenderHistoryOverlay_ShowsTimestamps()
    {
        var history = new SnapshotHistory(capacity: 10);
        history.Add(MakeResult("a"), new DateTime(2026, 3, 30, 14, 32, 5), runNumber: 1);

        var writer = new StringWriter();
        ScreenRenderer.RenderHistoryOverlay(writer, history, selectedIndex: 0, width: 80, height: 24);

        string output = writer.ToString();
        Assert.Contains("14:32:05", output);
    }

    [Fact]
    public void RenderHistoryOverlay_ShowsSelectionMarker()
    {
        var history = new SnapshotHistory(capacity: 10);
        history.Add(MakeResult("a"), DateTime.Now, runNumber: 1);
        history.Add(MakeResult("b"), DateTime.Now, runNumber: 2);

        var writer = new StringWriter();
        ScreenRenderer.RenderHistoryOverlay(writer, history, selectedIndex: 0, width: 80, height: 24);

        string output = writer.ToString();
        // Selected entry (index 0) should have the marker; the marker character is >
        // Since newest is at top, index 0 would be run #1 which is at the bottom of the list
        Assert.Contains(">", output);
    }

    [Fact]
    public void RenderHistoryOverlay_ShowsExitCode()
    {
        var history = new SnapshotHistory(capacity: 10);
        history.Add(MakeResult("a", exitCode: 1), DateTime.Now, runNumber: 1);

        var writer = new StringWriter();
        ScreenRenderer.RenderHistoryOverlay(writer, history, selectedIndex: 0, width: 80, height: 24);

        string output = writer.ToString();
        Assert.Contains("exit:1", output);
    }

    [Fact]
    public void RenderHistoryOverlay_ShowsDiffStats()
    {
        var history = new SnapshotHistory(capacity: 10);
        history.Add(MakeResult("line1\nline2"), DateTime.Now, runNumber: 1);
        history.Add(MakeResult("line1\nchanged"), DateTime.Now, runNumber: 2);

        var writer = new StringWriter();
        ScreenRenderer.RenderHistoryOverlay(writer, history, selectedIndex: 1, width: 80, height: 24);

        string output = writer.ToString();
        Assert.Contains("+1", output);
        Assert.Contains("-1", output);
    }

    [Fact]
    public void RenderHistoryOverlay_ShowsHistoryTitle()
    {
        var history = new SnapshotHistory(capacity: 10);
        history.Add(MakeResult("a"), DateTime.Now, runNumber: 1);

        var writer = new StringWriter();
        ScreenRenderer.RenderHistoryOverlay(writer, history, selectedIndex: 0, width: 80, height: 24);

        string output = writer.ToString();
        Assert.Contains("History", output);
    }

    [Fact]
    public void RenderHistoryOverlay_ShowsKeyHints()
    {
        var history = new SnapshotHistory(capacity: 10);
        history.Add(MakeResult("a"), DateTime.Now, runNumber: 1);

        var writer = new StringWriter();
        ScreenRenderer.RenderHistoryOverlay(writer, history, selectedIndex: 0, width: 80, height: 24);

        string output = writer.ToString();
        Assert.Contains("Enter", output);
        Assert.Contains("navigate", output);
    }

    [Fact]
    public void RenderHistoryOverlay_NewestAtTop()
    {
        var history = new SnapshotHistory(capacity: 10);
        history.Add(MakeResult("a"), new DateTime(2026, 3, 30, 14, 0, 0), runNumber: 1);
        history.Add(MakeResult("b"), new DateTime(2026, 3, 30, 14, 0, 2), runNumber: 2);
        history.Add(MakeResult("c"), new DateTime(2026, 3, 30, 14, 0, 4), runNumber: 3);

        var writer = new StringWriter();
        ScreenRenderer.RenderHistoryOverlay(writer, history, selectedIndex: 2, width: 80, height: 24);

        string output = writer.ToString();
        int pos3 = output.IndexOf("#3");
        int pos1 = output.IndexOf("#1");
        Assert.True(pos3 < pos1, "Newest (#3) should appear before oldest (#1)");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Winix.Peep.Tests/ --filter "HistoryOverlayTests"`
Expected: FAIL — `RenderHistoryOverlay` does not exist

- [ ] **Step 3: Implement RenderHistoryOverlay**

Add to `src/Winix.Peep/ScreenRenderer.cs`:

```csharp
    /// <summary>
    /// Renders the history overlay showing a scrollable list of snapshots.
    /// Newest snapshots appear at the top. The selected entry is marked with >.
    /// </summary>
    /// <param name="writer">Output writer.</param>
    /// <param name="history">The snapshot history to display.</param>
    /// <param name="selectedIndex">Index into history of the currently selected snapshot.</param>
    /// <param name="width">Terminal width in columns.</param>
    /// <param name="height">Terminal height in rows.</param>
    public static void RenderHistoryOverlay(
        TextWriter writer, SnapshotHistory history, int selectedIndex,
        int width, int height)
    {
        ClearScreen(writer);

        // Build list lines (newest first)
        var listLines = new List<string>();
        for (int i = history.Count - 1; i >= 0; i--)
        {
            Snapshot snap = history[i];
            string marker = (i == selectedIndex) ? ">" : " ";
            string exitStr = $"exit:{snap.Result.ExitCode}";
            string diffStr = $"+{snap.LinesAdded} -{snap.LinesRemoved}";
            string ts = snap.Timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

            listLines.Add($"  {marker} #{snap.RunNumber,-4} {ts}  {exitStr,-8} {diffStr}");
        }

        // Determine box dimensions
        int boxContentHeight = Math.Min(listLines.Count, Math.Max(1, height - 6)); // leave room for border + title + hints
        int boxHeight = boxContentHeight + 4; // top border + blank + content + hints + bottom border

        // Determine scroll window so selected item is visible
        // Find the display-list index (newest-first) of the selected snapshot
        int selectedDisplayIndex = history.Count - 1 - selectedIndex;
        int scrollStart = 0;
        if (selectedDisplayIndex >= boxContentHeight)
        {
            scrollStart = selectedDisplayIndex - boxContentHeight + 1;
        }

        // Centre vertically
        int topPadding = Math.Max(0, (height - boxHeight) / 2);
        for (int i = 0; i < topPadding; i++)
        {
            writer.WriteLine();
        }

        // Top border
        writer.WriteLine($"  +-- History {new string('-', Math.Max(0, 40))}+");

        // List entries
        for (int i = scrollStart; i < scrollStart + boxContentHeight && i < listLines.Count; i++)
        {
            writer.WriteLine(listLines[i]);
        }

        // Key hints
        writer.WriteLine();
        writer.WriteLine("  Up/Dn navigate  Enter select  t/Esc close");

        // Bottom border
        writer.WriteLine($"  +{new string('-', Math.Max(0, 53))}+");

        writer.Flush();
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Winix.Peep.Tests/ --filter "HistoryOverlayTests"`
Expected: All tests PASS

- [ ] **Step 5: Commit**

```
git add src/Winix.Peep/ScreenRenderer.cs tests/Winix.Peep.Tests/ScreenRendererTests.cs
git commit -m "feat: add history overlay rendering for peep time machine"
```

---

### Task 7: Update help overlay with time machine keys

**Files:**
- Modify: `src/Winix.Peep/ScreenRenderer.cs`
- Modify: `tests/Winix.Peep.Tests/ScreenRendererTests.cs`

- [ ] **Step 1: Write failing test for updated help text**

Append to `tests/Winix.Peep.Tests/ScreenRendererTests.cs`:

```csharp
public class HelpOverlayTimeMachineTests
{
    [Fact]
    public void RenderHelpOverlay_ShowsTimeMachineKeys()
    {
        var writer = new StringWriter();

        ScreenRenderer.RenderHelpOverlay(writer, width: 80, height: 24);

        string output = writer.ToString();
        Assert.Contains("Left/Right", output);
        Assert.Contains("time travel", output);
        Assert.Contains("history", output);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Winix.Peep.Tests/ --filter "HelpOverlayTimeMachineTests"`
Expected: FAIL — help text doesn't contain "Left/Right" or "time travel"

- [ ] **Step 3: Update help overlay text**

In `src/Winix.Peep/ScreenRenderer.cs`, find the `helpLines` array in `RenderHelpOverlay` and update it:

Replace:
```csharp
        string[] helpLines = new[]
        {
            "",
            "  Keyboard shortcuts:",
            "",
            "  q / Ctrl+C       Quit",
            "  Space            Pause/unpause",
            "  r / Enter        Force re-run",
            "  Up/Down          Scroll (when paused)",
            "  PgUp/PgDn        Scroll page (when paused)",
            "  d                Toggle diff highlighting",
            "  ? / Esc          Toggle this help",
            "",
        };
```

With:
```csharp
        string[] helpLines = new[]
        {
            "",
            "  Keyboard shortcuts:",
            "",
            "  q / Ctrl+C       Quit",
            "  Space            Pause/unpause",
            "  r / Enter        Force re-run",
            "  Up/Down          Scroll (when paused)",
            "  PgUp/PgDn        Scroll page (when paused)",
            "  Left/Right       Time travel (older/newer)",
            "  t                History overlay",
            "  d                Toggle diff highlighting",
            "  ? / Esc          Toggle this help",
            "",
        };
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Winix.Peep.Tests/ --filter "HelpOverlayTimeMachineTests"`
Expected: PASS

- [ ] **Step 5: Commit**

```
git add src/Winix.Peep/ScreenRenderer.cs tests/Winix.Peep.Tests/ScreenRendererTests.cs
git commit -m "feat: add time machine keybindings to peep help overlay"
```

---

### Task 8: Formatting — add history_retained to JSON

**Files:**
- Modify: `src/Winix.Peep/Formatting.cs`
- Modify: `tests/Winix.Peep.Tests/FormattingTests.cs`

- [ ] **Step 1: Write failing test for history_retained**

Append to `FormatJsonTests` class in `tests/Winix.Peep.Tests/FormattingTests.cs`:

```csharp
    [Fact]
    public void FormatJson_WithHistoryRetained_IncludesField()
    {
        string json = Formatting.FormatJson(
            exitCode: 0,
            exitReason: "manual",
            runs: 50,
            lastChildExitCode: 0,
            durationSeconds: 100.0,
            command: "dotnet test",
            lastOutput: null,
            toolName: "peep",
            version: "0.1.0",
            historyRetained: 50);

        Assert.Contains("\"history_retained\":50", json);
    }

    [Fact]
    public void FormatJson_NullHistoryRetained_OmitsField()
    {
        string json = Formatting.FormatJson(
            exitCode: 0,
            exitReason: "manual",
            runs: 5,
            lastChildExitCode: 0,
            durationSeconds: 10.0,
            command: "dotnet test",
            lastOutput: null,
            toolName: "peep",
            version: "0.1.0");

        Assert.DoesNotContain("history_retained", json);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Winix.Peep.Tests/ --filter "FormatJsonTests"`
Expected: FAIL — `FormatJson` does not accept `historyRetained` parameter

- [ ] **Step 3: Add historyRetained parameter to FormatJson**

In `src/Winix.Peep/Formatting.cs`, add an optional parameter to `FormatJson`:

After the `version` parameter, add:
```csharp
        int? historyRetained = null)
```

Before the closing `sb.Append('}');`, add:
```csharp
        if (historyRetained.HasValue)
        {
            sb.AppendFormat(CultureInfo.InvariantCulture,
                ",\"history_retained\":{0}", historyRetained.Value);
        }
```

- [ ] **Step 4: Run all formatting tests to verify they pass**

Run: `dotnet test tests/Winix.Peep.Tests/ --filter "FullyQualifiedName~Formatting"`
Expected: All PASS (existing tests unaffected — new param has a default)

- [ ] **Step 5: Commit**

```
git add src/Winix.Peep/Formatting.cs tests/Winix.Peep.Tests/FormattingTests.cs
git commit -m "feat: add history_retained field to peep JSON output"
```

---

### Task 9: Program.cs — --history arg parsing

**Files:**
- Modify: `src/peep/Program.cs`

- [ ] **Step 1: Add --history argument parsing**

In `src/peep/Program.cs`, add a new variable alongside the other arg declarations (after `bool noGitIgnore = false;`):

```csharp
    int historyCapacity = 1000;
```

Add a new case in the `switch (args[i])` block, after the `--no-gitignore` case:

```csharp
            case "--history":
                if (i + 1 >= args.Length || !int.TryParse(args[i + 1], out int parsedHistory))
                {
                    return WriteUsageError("--history requires a numeric argument", jsonOutput);
                }
                if (parsedHistory < 0)
                {
                    return WriteUsageError("--history must be non-negative", jsonOutput);
                }
                historyCapacity = parsedHistory;
                i++;
                break;
```

Pass `historyCapacity` through to `RunLoopAsync`. Add it as a new parameter after `diffEnabled`:

In the `RunLoopAsync` call, add `historyCapacity` after `diffEnabled`:
```csharp
    return await RunLoopAsync(
        command, commandArgs, commandDisplay,
        intervalSeconds, useInterval, watchPatterns.ToArray(), debounceMs,
        exitOnChange, exitOnSuccess, exitOnError, exitOnMatchRegexes, diffEnabled,
        historyCapacity, noGitIgnore, noHeader, jsonOutput, jsonOutputIncludeOutput, useColor, version);
```

In the `RunLoopAsync` signature, add `int historyCapacity` after `bool diffEnabled`:
```csharp
static async Task<int> RunLoopAsync(
    string command, string[] commandArgs, string commandDisplay,
    double intervalSeconds, bool useInterval, string[] watchPatterns, int debounceMs,
    bool exitOnChange, bool exitOnSuccess, bool exitOnError, Regex[] exitOnMatchRegexes,
    bool diffEnabled, int historyCapacity, bool noGitIgnore,
    bool noHeader, bool jsonOutput, bool jsonOutputIncludeOutput,
    bool useColor, string version)
```

- [ ] **Step 2: Add --history to help text**

In `PrintHelp()`, add after the `--no-gitignore` line:

```csharp
          --history N            Max history snapshots to retain (default: 1000, 0=unlimited)
```

- [ ] **Step 3: Verify it builds**

Run: `dotnet build src/peep/peep.csproj`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```
git add src/peep/Program.cs
git commit -m "feat: add --history arg to peep for time machine capacity"
```

---

### Task 10: Program.cs — time machine state and Left/Right key handling

**Files:**
- Modify: `src/peep/Program.cs`

This is the core event loop integration. Add time machine state, history recording, and step-mode key handling.

- [ ] **Step 1: Add SnapshotHistory and time machine state variables**

In `RunLoopAsync`, after the existing state variables (`bool running = false;`), add:

```csharp
    var history = new SnapshotHistory(historyCapacity);
    bool isTimeMachine = false;
```

- [ ] **Step 2: Record snapshots on every command completion**

There are three places where a command result is processed. In each, add a `history.Add()` call right after `runCount++`.

After the initial run block where `runCount++` and `lastResult = initialResult`:

```csharp
        history.Add(initialResult, DateTime.Now, runCount);
```

After the manual re-run block (inside `case ConsoleKey.R: / ConsoleKey.Enter:`) where `runCount++` and `lastResult = result`:

```csharp
                            history.Add(result, DateTime.Now, runCount);
```

After the scheduled run block (inside `if (shouldRun)`) where `runCount++` and `lastResult = result`:

```csharp
                    history.Add(result, DateTime.Now, runCount);
```

- [ ] **Step 3: Handle cursor preservation when adding during time machine**

In each of the two non-initial `history.Add()` call sites, wrap them to preserve cursor when in time machine mode:

```csharp
                    if (isTimeMachine)
                    {
                        int savedCursor = history.CursorIndex;
                        history.Add(result, DateTime.Now, runCount);
                        // Adjust cursor: if capacity was exceeded and oldest was evicted,
                        // the saved index needs to shift down by 1
                        if (history.CursorIndex != history.Count - 1)
                        {
                            // Add always moves to newest, so we restore
                        }
                        int adjustment = (history.Capacity > 0 && runCount > history.Capacity) ? 1 : 0;
                        int restored = Math.Max(0, savedCursor - adjustment);
                        // Navigate to restored position
                        history.MoveToNewest();
                        while (history.CursorIndex > restored)
                        {
                            history.MoveOlder();
                        }
                    }
                    else
                    {
                        history.Add(result, DateTime.Now, runCount);
                    }
```

Note: This replaces the plain `history.Add(result, DateTime.Now, runCount);` in the non-initial sites. The initial run site does not need this (time machine cannot be active on the first run).

- [ ] **Step 4: Add Left arrow key handler — enter/step time machine**

In the key switch block, add a new case after `ConsoleKey.PageDown`:

```csharp
                    case ConsoleKey.LeftArrow:
                        if (history.Count > 1)
                        {
                            if (!isTimeMachine)
                            {
                                // Enter time machine at second-newest
                                isTimeMachine = true;
                                isPaused = true;
                                scrollOffset = 0;
                                showHelp = false;
                                history.MoveOlder();
                            }
                            else
                            {
                                history.MoveOlder();
                            }

                            RenderTimeMachineScreen(history, commandDisplay, intervalSeconds,
                                watchPatterns, noHeader, useColor, scrollOffset, diffEnabled);
                        }
                        break;
```

- [ ] **Step 5: Add Right arrow key handler — step newer / exit time machine**

```csharp
                    case ConsoleKey.RightArrow:
                        if (isTimeMachine)
                        {
                            history.MoveNewer();
                            if (history.IsAtNewest)
                            {
                                // Exit time machine, return to live
                                isTimeMachine = false;
                                isPaused = false;
                                scrollOffset = 0;
                                RenderScreen(lastResult, commandDisplay, intervalSeconds, watchPatterns,
                                    runCount, isPaused, noHeader, useColor, scrollOffset,
                                    diffEnabled, previousOutput);
                            }
                            else
                            {
                                RenderTimeMachineScreen(history, commandDisplay, intervalSeconds,
                                    watchPatterns, noHeader, useColor, scrollOffset, diffEnabled);
                            }
                        }
                        break;
```

- [ ] **Step 6: Update Space and Escape to exit time machine**

In the `ConsoleKey.Spacebar` case, add time machine exit logic at the top:

```csharp
                    case ConsoleKey.Spacebar:
                        if (isTimeMachine)
                        {
                            // Exit time machine, return to live
                            isTimeMachine = false;
                            isPaused = false;
                            scrollOffset = 0;
                            history.MoveToNewest();
                            RenderScreen(lastResult, commandDisplay, intervalSeconds, watchPatterns,
                                runCount, isPaused, noHeader, useColor, scrollOffset,
                                diffEnabled, previousOutput);
                            break;
                        }
                        isPaused = !isPaused;
                        // ... rest of existing code
```

In the `ConsoleKey.Escape` case, update to also exit time machine:

```csharp
                    case ConsoleKey.Escape:
                        if (isTimeMachine)
                        {
                            isTimeMachine = false;
                            isPaused = false;
                            scrollOffset = 0;
                            history.MoveToNewest();
                            RenderScreen(lastResult, commandDisplay, intervalSeconds, watchPatterns,
                                runCount, isPaused, noHeader, useColor, scrollOffset,
                                diffEnabled, previousOutput);
                        }
                        else if (showHelp)
                        {
                            showHelp = false;
                            RenderScreen(lastResult, commandDisplay, intervalSeconds, watchPatterns,
                                runCount, isPaused, noHeader, useColor, scrollOffset,
                                    diffEnabled, previousOutput);
                        }
                        break;
```

- [ ] **Step 7: Update scroll keys to work in time machine mode**

Update the `ConsoleKey.UpArrow` case to also work when in time machine (time machine implies paused):

Replace `if (isPaused)` with `if (isPaused || isTimeMachine)` in all four scroll cases (UpArrow, DownArrow, PageUp, PageDown).

When in time machine, re-render with `RenderTimeMachineScreen` instead of `RenderScreen`. The simplest approach: after the scroll offset change, check `isTimeMachine`:

```csharp
                    case ConsoleKey.UpArrow:
                        if (isPaused || isTimeMachine)
                        {
                            scrollOffset = Math.Max(0, scrollOffset - 1);
                            if (isTimeMachine)
                            {
                                RenderTimeMachineScreen(history, commandDisplay, intervalSeconds,
                                    watchPatterns, noHeader, useColor, scrollOffset, diffEnabled);
                            }
                            else
                            {
                                RenderScreen(lastResult, commandDisplay, intervalSeconds, watchPatterns,
                                    runCount, isPaused, noHeader, useColor, scrollOffset,
                                    diffEnabled, previousOutput);
                            }
                        }
                        break;
```

Apply the same pattern to DownArrow, PageUp, and PageDown.

- [ ] **Step 8: Add RenderTimeMachineScreen helper function**

Add a new static function alongside the existing `RenderScreen`:

```csharp
static void RenderTimeMachineScreen(
    SnapshotHistory history, string commandDisplay,
    double intervalSeconds, string[] watchPatterns,
    bool noHeader, bool useColor, int scrollOffset, bool diffEnabled)
{
    Snapshot current = history.Current;
    Snapshot? previous = history.GetPreviousOf(history.CursorIndex);

    string? header = noHeader ? null : ScreenRenderer.FormatHeader(
        intervalSeconds, commandDisplay, current.Timestamp,
        current.Result.ExitCode, current.RunNumber, isPaused: true, useColor,
        isDiffEnabled: diffEnabled,
        isTimeMachine: true,
        timeMachinePosition: history.CursorIndex + 1,
        timeMachineTotal: history.Count);

    string? watchLine = noHeader ? null : ScreenRenderer.FormatWatchLine(watchPatterns, useColor);

    ScreenRenderer.Render(
        Console.Out,
        header,
        watchLine,
        current.Result.Output,
        GetTerminalHeight(),
        scrollOffset,
        showHeader: !noHeader,
        previousOutput: diffEnabled ? previous?.Result.Output : null,
        diffEnabled: diffEnabled);
}
```

- [ ] **Step 9: Verify it builds**

Run: `dotnet build src/peep/peep.csproj`
Expected: Build succeeded

- [ ] **Step 10: Commit**

```
git add src/peep/Program.cs
git commit -m "feat: add time machine step mode (Left/Right arrows) to peep event loop"
```

---

### Task 11: Program.cs — history overlay (t key) and selection navigation

**Files:**
- Modify: `src/peep/Program.cs`

- [ ] **Step 1: Add overlay state variables**

In `RunLoopAsync`, alongside the time machine state variables, add:

```csharp
    bool historyOverlayOpen = false;
    int historyOverlaySelection = -1; // index into history, set on overlay open
```

- [ ] **Step 2: Add t key handler**

In the `default:` case of the key switch (where `?` is checked via `key.KeyChar`), add a check for `t` before the `?` check:

```csharp
                    default:
                        if (key.KeyChar == 't')
                        {
                            if (historyOverlayOpen)
                            {
                                // Close overlay
                                historyOverlayOpen = false;
                                if (isTimeMachine)
                                {
                                    RenderTimeMachineScreen(history, commandDisplay, intervalSeconds,
                                        watchPatterns, noHeader, useColor, scrollOffset, diffEnabled);
                                }
                                else
                                {
                                    RenderScreen(lastResult, commandDisplay, intervalSeconds, watchPatterns,
                                        runCount, isPaused, noHeader, useColor, scrollOffset,
                                        diffEnabled, previousOutput);
                                }
                            }
                            else if (history.Count > 0)
                            {
                                // Open overlay — enter time machine if not already
                                if (!isTimeMachine)
                                {
                                    isTimeMachine = true;
                                    isPaused = true;
                                    scrollOffset = 0;
                                    showHelp = false;
                                }
                                historyOverlayOpen = true;
                                historyOverlaySelection = history.CursorIndex;
                                ScreenRenderer.RenderHistoryOverlay(Console.Out, history,
                                    historyOverlaySelection, GetTerminalWidth(), GetTerminalHeight());
                            }
                        }
                        else if (key.KeyChar == '?')
                        {
                            // ... existing ? handling
                        }
                        break;
```

- [ ] **Step 3: Override Up/Down/Enter/Escape when overlay is open**

At the very top of the key handling, before the existing switch, add an overlay intercept:

```csharp
                if (historyOverlayOpen)
                {
                    switch (key.Key)
                    {
                        case ConsoleKey.UpArrow:
                            // Move selection towards newer (lower index in display = higher index in history)
                            if (historyOverlaySelection < history.Count - 1)
                            {
                                historyOverlaySelection++;
                            }
                            ScreenRenderer.RenderHistoryOverlay(Console.Out, history,
                                historyOverlaySelection, GetTerminalWidth(), GetTerminalHeight());
                            break;

                        case ConsoleKey.DownArrow:
                            // Move selection towards older
                            if (historyOverlaySelection > 0)
                            {
                                historyOverlaySelection--;
                            }
                            ScreenRenderer.RenderHistoryOverlay(Console.Out, history,
                                historyOverlaySelection, GetTerminalWidth(), GetTerminalHeight());
                            break;

                        case ConsoleKey.Enter:
                            // Jump to selected snapshot
                            history.MoveToNewest();
                            while (history.CursorIndex > historyOverlaySelection)
                            {
                                history.MoveOlder();
                            }
                            historyOverlayOpen = false;
                            scrollOffset = 0;
                            RenderTimeMachineScreen(history, commandDisplay, intervalSeconds,
                                watchPatterns, noHeader, useColor, scrollOffset, diffEnabled);
                            break;

                        case ConsoleKey.Escape:
                            historyOverlayOpen = false;
                            RenderTimeMachineScreen(history, commandDisplay, intervalSeconds,
                                watchPatterns, noHeader, useColor, scrollOffset, diffEnabled);
                            break;

                        default:
                            if (key.KeyChar == 't')
                            {
                                historyOverlayOpen = false;
                                RenderTimeMachineScreen(history, commandDisplay, intervalSeconds,
                                    watchPatterns, noHeader, useColor, scrollOffset, diffEnabled);
                            }
                            break;
                    }
                    continue; // skip normal key handling when overlay is open
                }
```

This block goes right after `var key = Console.ReadKey(intercept: true);` and before `switch (key.Key)`.

- [ ] **Step 4: Update Escape handler to also close overlay**

The overlay intercept block already handles Escape when the overlay is open. No additional change needed.

- [ ] **Step 5: Suppress normal re-run and screen updates when Enter is pressed during overlay**

The overlay intercept's `continue` statement prevents the main switch from running, so Enter in the overlay only selects a snapshot, it does not trigger a re-run.

- [ ] **Step 6: Verify it builds**

Run: `dotnet build src/peep/peep.csproj`
Expected: Build succeeded

- [ ] **Step 7: Commit**

```
git add src/peep/Program.cs
git commit -m "feat: add history overlay (t key) to peep time machine"
```

---

### Task 12: Program.cs — pass history_retained to JSON output

**Files:**
- Modify: `src/peep/Program.cs`

- [ ] **Step 1: Pass historyRetained to FormatJson**

In `HandleExitFromLoop`, the function needs access to history count. Add `int historyRetained` as a new parameter:

```csharp
static int HandleExitFromLoop(
    Stopwatch sessionStopwatch, int runCount, PeepResult? lastResult,
    string commandDisplay, bool jsonOutput, bool jsonOutputIncludeOutput,
    string version, string exitReason, int historyRetained)
```

In the `FormatJson` call inside `HandleExitFromLoop`, add:
```csharp
            historyRetained: historyRetained));
```

Update all call sites of `HandleExitFromLoop` to pass `history.Count`:
- After initial run failure: `history.Count` (will be 0)
- After initial auto-exit check: `history.Count`
- At end of RunLoopAsync: `history.Count`

For the initial run failure (before history exists), pass `0` since the variable `history` is declared after the initial run block. Actually, move the `history` declaration to before the initial run, alongside the other state variables, so it's always available.

- [ ] **Step 2: Verify it builds and existing tests pass**

Run: `dotnet build src/peep/peep.csproj`
Expected: Build succeeded

Run: `dotnet test tests/Winix.Peep.Tests/`
Expected: All tests PASS

- [ ] **Step 3: Commit**

```
git add src/peep/Program.cs
git commit -m "feat: pass history_retained to peep JSON output"
```

---

### Task 13: Update previousOutput tracking for diff mode

**Files:**
- Modify: `src/peep/Program.cs`

The existing `previousOutput` variable tracks the prior run's output for diff highlighting. With time machine, we need to ensure:
1. In live mode, `previousOutput` continues to work as before.
2. In time machine mode, diff uses `history.GetPreviousOf()` (already handled by `RenderTimeMachineScreen`).

- [ ] **Step 1: Update previousOutput assignment**

Currently `previousOutput` is only set once (after the initial run). It should be updated after every run so live-mode diff works correctly. In each place where `lastResult = result` is set after a command run, add:

```csharp
                        previousOutput = prevOutput;
```

This should already be happening via the `prevOutput` local variable. Verify the three run sites:

1. **Initial run** (line ~337): `previousOutput = initialResult.Output;` — this sets it to the *initial* output. But for the second run's diff, we need the previous (initial) output. This is correct — the first diff will compare run 2 to run 1's output stored in `previousOutput`.

2. **Manual re-run** (line ~409): `string? prevOutput = lastResult?.Output;` is captured before the run, but `previousOutput` is never updated to become the *previous* output for the next diff. After the run completes and `lastResult = result`, add:
```csharp
                            previousOutput = prevOutput;
```

3. **Scheduled run** (line ~544): Same pattern. After `lastResult = result`, add:
```csharp
                    previousOutput = prevOutput;
```

Wait — check the existing code more carefully. The current code passes `previousOutput` to `RenderScreen`, but it's the original output from run 1. This means diff mode currently compares every run against the *first* run, not the *previous* run. Let me re-read...

Looking at the existing code: `previousOutput = initialResult.Output;` is set once. It is never updated. When `RenderScreen` is called with `previousOutput`, it passes it to `ScreenRenderer.Render` as the diff baseline. This means diff always compares against the initial run's output.

This is actually the existing behaviour and matches `watch -d` (which highlights changes from the initial output). If you want `watch -d` semantics (cumulative diff from initial), leave it alone. If you want viddy semantics (diff from previous run), update it. The design spec says "compares current snapshot to its predecessor" which is viddy-style.

Fix: update `previousOutput` after each run.

- [ ] **Step 2: Update previousOutput after manual and scheduled runs**

In the manual re-run block, after `lastResult = result;`:
```csharp
                            previousOutput = prevOutput;
```

In the scheduled run block, after `lastResult = result;`:
```csharp
                    previousOutput = prevOutput;
```

- [ ] **Step 3: Verify it builds**

Run: `dotnet build src/peep/peep.csproj`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```
git add src/peep/Program.cs
git commit -m "fix: update previousOutput after each run so diff compares consecutive runs"
```

---

### Task 14: Full build and test verification

**Files:** None (verification only)

- [ ] **Step 1: Full build**

Run: `dotnet build Winix.sln`
Expected: Build succeeded, 0 errors, 0 warnings

- [ ] **Step 2: Full test suite**

Run: `dotnet test Winix.sln`
Expected: All tests pass (existing + new). Total should be ~270+ tests (existing 245 + ~25 new).

- [ ] **Step 3: AOT publish verification**

Run: `dotnet publish src/peep/peep.csproj -c Release -r win-x64`
Expected: Successful AOT publish, no trim warnings related to new code.

- [ ] **Step 4: Commit plan as complete (if any fixups were needed)**

If any fixes were applied during verification, commit them:
```
git add -A
git commit -m "fix: address build/test issues from time machine integration"
```
