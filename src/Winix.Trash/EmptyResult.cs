namespace Winix.Trash;

/// <summary>Outcome of emptying the trash.</summary>
/// <param name="ItemsRemoved">Number of top-level items confirmed permanently removed (the data is
/// gone). On Windows this is approximate — the OS empty API is not per-item attributable (see README).</param>
/// <param name="FailedCount">Number of top-level items that could NOT be removed (e.g. permission
/// denied, busy). When non-zero, <c>Cli</c> surfaces it and exits 1 — an item is never counted as
/// removed unless its data was actually deleted, so this is the channel for "could not empty X".</param>
public sealed record EmptyResult(int ItemsRemoved, int FailedCount = 0);
