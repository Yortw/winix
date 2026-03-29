# Peep Time Machine — Design Spec

**Date:** 2026-03-30
**Status:** Approved
**Related:** [Peep design](2026-03-29-peep-design.md), [CLI conventions](2026-03-29-winix-cli-conventions.md)

## Overview

Add a "time machine" feature to peep that lets users browse back through historical command output snapshots during a session. Inspired by viddy's time machine but adapted to peep's minimal, full-width philosophy.

**Core principle:** Zero screen cost during normal use. History is always being collected silently. Users access it via hotkeys (step mode) or a modal overlay (pick mode).

## Data Model

### Snapshot record

```csharp
public sealed record Snapshot(
    PeepResult Result,
    DateTime Timestamp,     // wall clock when run started
    int RunNumber,          // 1-based sequential run number
    int LinesAdded,         // diff stats vs previous snapshot
    int LinesRemoved        // diff stats vs previous snapshot
);
```

Diff stats are computed at insert time (comparing ANSI-stripped output to the previous snapshot) and cached. First snapshot has `+N -0` where N is line count.

### SnapshotHistory class

Bounded in-memory collection with cursor navigation.

```
SnapshotHistory(int capacity = 1000)
```

**Storage:** `List<Snapshot>`. When count exceeds capacity, oldest entry (index 0) is removed. A circular array would avoid the shift but `RemoveAt(0)` on 1000 items is microseconds — not worth the complexity.

**Public API:**

| Member | Description |
|--------|-------------|
| `Add(PeepResult, DateTime)` | Append snapshot, compute diff stats, drop oldest if at capacity |
| `Count` | Number of stored snapshots |
| `Capacity` | Max snapshots (0 = unlimited) |
| `this[int index]` | Indexer: 0 = oldest, Count-1 = newest |
| `Current` | Snapshot at cursor |
| `CursorIndex` | Current cursor position |
| `IsAtNewest` | `CursorIndex == Count - 1` |
| `MoveOlder() -> bool` | Cursor--, returns false if already at oldest |
| `MoveNewer() -> bool` | Cursor++, returns false if already at newest |
| `MoveToOldest()` | Cursor = 0 |
| `MoveToNewest()` | Cursor = Count - 1 |
| `GetPreviousOf(int index)` | Returns snapshot at index-1, or null if index is 0 |

When `Add()` is called, cursor always moves to newest (live tracking). If the user is in time machine mode, the event loop preserves the cursor before `Add` and restores it after (adjusting for any overflow eviction).

## Navigation Modes

### Step mode (Left/Right arrows)

No overlay, no screen cost. The header updates to show position.

**Entering time machine:**
- `Left` arrow when not in time machine → enters time machine, auto-pauses, steps to second-newest snapshot

**While in time machine:**
- `Left` → step older (`MoveOlder()`)
- `Right` → step newer; if at newest, exits time machine and unpauses (back to live)
- `Space` → exit time machine, unpause (back to live)
- `Esc` → exit time machine, unpause (back to live)
- `Up/Down/PgUp/PgDn` → scroll within the current snapshot (same as existing pause scroll)
- `d` → toggle diff highlighting (compares current snapshot to its predecessor)
- `r/Enter` → force re-run; new result added to history, cursor stays where it is
- `t` → open history overlay

**Exiting time machine:**
- `Right` when at newest, `Space`, or `Esc` → return to live, unpause, scroll reset to 0

### History overlay (t key)

Modal overlay rendered on top of output, similar to the existing `?` help overlay.

**Entry:** `t` key while in time machine mode. If not in time machine, `t` enters time machine and opens overlay simultaneously.

**Layout:**

```
+-- History ------------------------------------+
|                                               |
|   > #17  14:32:05  exit:0      +3 -1          |
|     #16  14:32:03  exit:0      +0 -0          |
|     #15  14:32:01  exit:1      +2 -0          |
|     #14  14:31:59  exit:0      +0 -0          |
|     ...                                       |
|                                               |
|  Up/Dn navigate  Enter select  t/Esc close    |
+---------=-------------------------------------+
```

**Behaviour:**
- Vertically centred box, width adapts to terminal
- Newest at top, oldest at bottom
- `>` marker on the currently selected entry
- `Up/Down` moves selection (list scrolls to keep selection visible)
- `Enter` jumps to selected snapshot, dismisses overlay
- `t` or `Esc` dismisses without changing cursor position
- Exit code shown in red if non-zero
- Diff stats: green `+N`, red `-N`

**Rendering:** New static method `ScreenRenderer.RenderHistoryOverlay(TextWriter, SnapshotHistory, int selectedIndex, int terminalWidth, int terminalHeight)`. Follows the pattern of `RenderHelpOverlay`.

## Header Changes

When in time machine mode, the header shows the selected snapshot's context instead of live state:

- Timestamp reflects the snapshot's `Timestamp`, not wall clock
- Run count shows `[run #3/17]` — current position out of total
- `[TIME]` indicator appears (alongside existing `[PAUSED]`, `[DIFF]`)
- Exit code reflects the snapshot's exit code

When not in time machine, header is unchanged from today.

## Event Loop Integration

**New state variables in Program.cs:**
- `isTimeMachine` (bool)
- `historyOverlayOpen` (bool)
- `historyOverlaySelection` (int) — selected row in overlay (independent of history cursor until Enter)
- `SnapshotHistory` instance

**On command completion:**
1. `history.Add(result, DateTime.Now)`
2. If not in time machine: display updates as today (latest result)
3. If in time machine: display stays on current cursor position; new snapshot is silently added

**Pause interaction:**
- Entering time machine auto-pauses
- Exiting time machine unpauses and returns to live
- If already paused when pressing `Left`, enter time machine from current (latest) snapshot

## CLI Arguments

| Argument | Description | Default |
|----------|-------------|---------|
| `--history N` | Max snapshots to retain. 0 = unlimited. | 1000 |

No flag to enable/disable time machine — always available.

## JSON Output Changes

One new field in the session summary:

```json
{
  "runs": 17,
  "history_retained": 17
}
```

- `runs`: unchanged — total command executions in session
- `history_retained` (new): snapshots still in the buffer (may be less than `runs` if history capacity was exceeded)

## Help Overlay Update

Add to the existing `?` help text:

```
Left/Right   time travel (older/newer)
t            history overlay
```

## Testing

### SnapshotHistory unit tests (new file: SnapshotHistoryTests.cs)

- Add and retrieve snapshots (oldest/newest ordering)
- Cursor navigation: older, newer, oldest, newest
- Bounds checking: MoveOlder at oldest returns false, MoveNewer at newest returns false
- Capacity enforcement: oldest dropped, count stays at capacity
- Diff stats: computed correctly, first snapshot has correct values
- Empty history: Current throws or returns null, moves return false
- GetPreviousOf: returns correct snapshot, returns null for index 0

### ScreenRenderer tests (additions)

- RenderHistoryOverlay: formatting, selection marker, scroll when list exceeds visible area
- Header format with `[TIME]` indicator and snapshot position `[run #3/17]`
- Header uses snapshot timestamp and exit code when in time machine

### Formatting tests (additions)

- JSON output includes `total_runs` and `history_retained` fields

## Out of Scope

- Persistence / replay after exit (`--save`, `--load`) — can add later
- Jump to snapshot at specific time — no UI mechanism yet, data structure supports it
- Horizontal timeline / progress bar — step mode + overlay covers the UX
- Snapshot compression or deduplication — premature optimisation
