#nullable enable
using Xunit;

namespace Winix.Contract.Tests;

/// <summary>
/// Contract-lock snapshot tests. The registry (<see cref="DescribeSurfaces.All"/>) and
/// capture helper (<see cref="ConsoleCapture"/>) are built in this task (Task 7).
/// Task 8 probes subcommand surfaces; Task 9 adds the real snapshot-comparison tests
/// and commits the baseline snapshots.
/// </summary>
public sealed class ContractSnapshotTests
{
    /// <summary>
    /// Smoke check: the registry must be non-empty so a future task cannot accidentally
    /// produce a no-op test run by clearing All.
    /// </summary>
    [Fact]
    public void Registry_IsNonEmpty()
    {
        Assert.True(DescribeSurfaces.All.Count > 0,
            "DescribeSurfaces.All must contain at least one entry; the registry appears to have been cleared.");
    }
}
