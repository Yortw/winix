namespace Winix.Peep;

/// <summary>
/// Ring-buffer of <see cref="Snapshot"/> entries collected during a peep session,
/// supporting time-machine navigation (cursor moves between oldest and newest).
/// </summary>
/// <remarks>
/// The history is ordered oldest-first: index 0 is the oldest retained snapshot,
/// index <see cref="Count"/>-1 is the newest. When <see cref="Capacity"/> is
/// exceeded the oldest entry is evicted, shifting all indices down by one.
/// </remarks>
public sealed class SnapshotHistory
{
    private readonly int _capacity;
    private readonly List<Snapshot> _snapshots = new();
    private int _cursorIndex = -1;

    /// <summary>
    /// Initialises a new instance with the specified capacity.
    /// </summary>
    /// <param name="capacity">
    /// Maximum number of snapshots to retain. 0 means unlimited.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="capacity"/> is negative.
    /// </exception>
    public SnapshotHistory(int capacity)
    {
        if (capacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be 0 (unlimited) or positive.");
        }

        _capacity = capacity;
    }

    /// <summary>
    /// Maximum number of snapshots retained. 0 means unlimited.
    /// </summary>
    public int Capacity => _capacity;

    /// <summary>
    /// Number of snapshots currently in the history.
    /// </summary>
    public int Count => _snapshots.Count;

    /// <summary>
    /// The snapshot the cursor currently points at.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the history is empty.</exception>
    public Snapshot Current
    {
        get
        {
            if (_snapshots.Count == 0)
            {
                throw new InvalidOperationException("History is empty.");
            }

            return _snapshots[_cursorIndex];
        }
    }

    /// <summary>
    /// Zero-based index of the cursor into the snapshot list (0 = oldest).
    /// Returns -1 when the history is empty.
    /// </summary>
    public int CursorIndex => _cursorIndex;

    /// <summary>
    /// True when the cursor is positioned at the newest (most recent) snapshot,
    /// or when the history is empty.
    /// </summary>
    public bool IsAtNewest => _snapshots.Count == 0 || _cursorIndex == _snapshots.Count - 1;

    /// <summary>
    /// Returns the snapshot at position <paramref name="index"/> (0 = oldest).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="index"/> is out of range.
    /// </exception>
    public Snapshot this[int index] => _snapshots[index];

    /// <summary>
    /// Appends a new snapshot, computing diff stats against the previous entry.
    /// Evicts the oldest snapshot first if the history is at capacity.
    /// Moves the cursor to the newly added (newest) snapshot.
    /// </summary>
    /// <param name="result">The command execution result.</param>
    /// <param name="timestamp">Wall clock time when the run started.</param>
    /// <param name="runNumber">1-based sequential run number within the session.</param>
    public void Add(PeepResult result, DateTime timestamp, int runNumber)
    {
        var (linesAdded, linesRemoved) = ComputeDiff(result);

        var snapshot = new Snapshot(result, timestamp, runNumber, linesAdded, linesRemoved);

        if (_capacity > 0 && _snapshots.Count == _capacity)
        {
            // O(n) shift — acceptable at realistic capacities (≤1000); not worth a circular buffer.
            _snapshots.RemoveAt(0);
        }

        _snapshots.Add(snapshot);
        _cursorIndex = _snapshots.Count - 1;
    }

    /// <summary>
    /// Moves the cursor one step toward the older end of the history.
    /// </summary>
    /// <returns>
    /// True if the cursor moved; false if already at the oldest snapshot or the history is empty.
    /// </returns>
    public bool MoveOlder()
    {
        if (_snapshots.Count == 0 || _cursorIndex == 0)
        {
            return false;
        }

        _cursorIndex--;
        return true;
    }

    /// <summary>
    /// Moves the cursor one step toward the newer end of the history.
    /// </summary>
    /// <returns>
    /// True if the cursor moved; false if already at the newest snapshot or the history is empty.
    /// </returns>
    public bool MoveNewer()
    {
        if (_snapshots.Count == 0 || _cursorIndex == _snapshots.Count - 1)
        {
            return false;
        }

        _cursorIndex++;
        return true;
    }

    /// <summary>
    /// Moves the cursor to the oldest snapshot (index 0).
    /// No-op when the history is empty.
    /// </summary>
    public void MoveToOldest()
    {
        if (_snapshots.Count > 0)
        {
            _cursorIndex = 0;
        }
    }

    /// <summary>
    /// Moves the cursor to the newest snapshot (index Count-1).
    /// No-op when the history is empty.
    /// </summary>
    public void MoveToNewest()
    {
        if (_snapshots.Count > 0)
        {
            _cursorIndex = _snapshots.Count - 1;
        }
    }

    /// <summary>
    /// Returns the snapshot immediately before position <paramref name="index"/>,
    /// or null if <paramref name="index"/> is 0 or out of range.
    /// </summary>
    /// <param name="index">Zero-based index of the reference snapshot (0 = oldest).</param>
    public Snapshot? GetPreviousOf(int index)
    {
        if (index <= 0 || index >= _snapshots.Count)
        {
            return null;
        }

        return _snapshots[index - 1];
    }

    /// <summary>
    /// Computes line-level diff stats between the new result and the most recent
    /// snapshot already in the list. ANSI sequences are stripped before comparison.
    /// </summary>
    /// <remarks>
    /// Uses a multiset diff: for each unique line, the excess in the current output
    /// counts as added and the excess in the previous output counts as removed.
    /// A changed line therefore contributes 1 add and 1 remove.
    /// </remarks>
    private (int LinesAdded, int LinesRemoved) ComputeDiff(PeepResult result)
    {
        var currentLines = SplitLines(Formatting.StripAnsi(result.Output));

        if (_snapshots.Count == 0)
        {
            // First snapshot: every line is "added", nothing removed.
            return (currentLines.Length, 0);
        }

        var previousLines = SplitLines(Formatting.StripAnsi(_snapshots[_snapshots.Count - 1].Result.Output));

        // Build frequency maps, then compute per-line excess.
        var currentCounts = BuildLineCounts(currentLines);
        var previousCounts = BuildLineCounts(previousLines);

        int added = 0;
        int removed = 0;

        // Lines in current that exceed what's in previous.
        foreach (var (line, count) in currentCounts)
        {
            previousCounts.TryGetValue(line, out int prevCount);
            int excess = count - prevCount;
            if (excess > 0)
            {
                added += excess;
            }
        }

        // Lines in previous that exceed what's in current.
        foreach (var (line, count) in previousCounts)
        {
            currentCounts.TryGetValue(line, out int currCount);
            int excess = count - currCount;
            if (excess > 0)
            {
                removed += excess;
            }
        }

        return (added, removed);
    }

    private static string[] SplitLines(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Array.Empty<string>();
        }

        // Normalise CRLF to LF before splitting so Windows output doesn't
        // produce spurious empty lines at the end of each CRLF-terminated line.
        // Trim a single trailing empty element: virtually all command output ends
        // with \n, which causes Split to produce a final "" that inflates line counts.
        // We only remove the last element — intentional blank lines in the middle are preserved.
        var parts = text.Replace("\r\n", "\n").Split('\n');
        if (parts.Length > 0 && parts[parts.Length - 1].Length == 0)
        {
            Array.Resize(ref parts, parts.Length - 1);
        }

        return parts;
    }

    private static Dictionary<string, int> BuildLineCounts(string[] lines)
    {
        var counts = new Dictionary<string, int>(lines.Length, StringComparer.Ordinal);
        foreach (var line in lines)
        {
            counts.TryGetValue(line, out int current);
            counts[line] = current + 1;
        }

        return counts;
    }
}
