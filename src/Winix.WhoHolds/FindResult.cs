#nullable enable

using System;
using System.Collections.Generic;

namespace Winix.WhoHolds;

/// <summary>
/// Outcome of a Find* query on one of the lock-finder backends.
/// </summary>
/// <remarks>
/// Distinguishes "query succeeded with zero holders" (exit 0) from "the backend API
/// itself errored" (exit 1). Pre-FindResult, both cases collapsed to an empty list and
/// the documented exit-1 path was unreachable from the CLI — see SFH I1+I2+I3 in the
/// 2026-05-08 round-1 review.
/// </remarks>
/// <param name="Results">
/// Processes found holding the queried resource. Empty when no holders were found, or when
/// <see cref="QueryFailed"/> is <see langword="true"/>.
/// </param>
/// <param name="QueryFailed">
/// <see langword="true"/> when the underlying backend (Restart Manager, IP Helper, lsof) failed
/// or could not be invoked. The empty list in <see cref="Results"/> is then meaningless and
/// must not be reported as "no holders" — this distinction is what gates exit code 1 in the
/// whoholds CLI per its documented exit-code contract.
/// </param>
/// <param name="Reason">
/// Human-readable description of the failure, when <see cref="QueryFailed"/> is
/// <see langword="true"/>. Always <see langword="null"/> on success, even when
/// <see cref="Results"/> is empty.
/// </param>
public sealed record FindResult(
    IReadOnlyList<LockInfo> Results,
    bool QueryFailed,
    string? Reason)
{
    /// <summary>An empty success result — no holders found, query succeeded.</summary>
    public static FindResult Empty { get; } = new(Array.Empty<LockInfo>(), false, null);

    /// <summary>
    /// Creates a successful query result with the supplied holders.
    /// Pass an empty list to indicate "query ran cleanly, found nothing" — distinct from
    /// <see cref="Failed"/>.
    /// </summary>
    public static FindResult Success(IReadOnlyList<LockInfo> results) =>
        new(results, false, null);

    /// <summary>
    /// Creates a failure result with the supplied human-readable reason. The reason is
    /// surfaced on stderr by the CLI; do not include the resource name or other content
    /// the caller already knows.
    /// </summary>
    public static FindResult Failed(string reason) =>
        new(Array.Empty<LockInfo>(), true, reason);
}
