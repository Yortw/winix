#nullable enable

namespace Winix.TreeX;

/// <summary>
/// A single error encountered while walking a directory tree. Collected by
/// <see cref="TreeBuilder"/> and surfaced to stderr by the CLI layer, which also sets
/// exit code 1 when at least one error occurred — implementing the README's documented
/// exit-1 contract for "Runtime error (permission denied, invalid path)".
/// </summary>
/// <remarks>
/// Round-1 fresh-eyes 2026-05-09 silent-failure-hunter C1: pre-fix, the catch sites in
/// <see cref="TreeBuilder.BuildChildren"/> silently <c>return</c>'d / <c>continue</c>'d
/// on permission errors, so a partial tree shipped with no diagnostic and exit code 0.
/// Real <c>tree(1)</c> prints <c>[error opening dir]</c> per inaccessible node and
/// returns a non-zero exit code on any walk failure.
/// </remarks>
/// <param name="Path">Path that could not be read (directory or file).</param>
/// <param name="Reason">Human-readable reason (permission denied, vanished, I/O error).</param>
public sealed record WalkError(string Path, string Reason);
