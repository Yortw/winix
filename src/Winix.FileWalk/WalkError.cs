#nullable enable

namespace Winix.FileWalk;

/// <summary>
/// A single error encountered while walking a directory tree. Collected by
/// <see cref="FileWalker"/> via <see cref="FileWalker.WalkErrors"/> and surfaced to
/// stderr by the CLI layer, which also sets exit code 1 when at least one error
/// occurred — implementing the documented exit-1 contract for "permission denied,
/// invalid path, partial walk."
/// </summary>
/// <remarks>
/// Round-1 fresh-eyes 2026-05-09 silent-failure-hunter C1 (files): pre-fix, the catch
/// sites in <see cref="FileWalker.WalkDirectory"/> silently <c>yield break</c>'d /
/// <c>continue</c>'d on permission errors and partial trees shipped with no diagnostic
/// and exit code 0. Same defect class closed in treex round-stop a few hours earlier
/// (treex's <see cref="Winix.TreeX.WalkError"/> follows the same shape; future refactor
/// could consolidate them).
/// </remarks>
/// <param name="Path">Path that could not be read (directory or file).</param>
/// <param name="Reason">Human-readable reason (permission denied, vanished, I/O error).</param>
public sealed record WalkError(string Path, string Reason);
