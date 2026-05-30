using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Winix.Trash;

/// <summary>Outcome of a trash operation over one or more paths.</summary>
public sealed class TrashResult
{
    /// <summary>Per-path outcomes in input order.</summary>
    public required IReadOnlyList<PathOutcome> Outcomes { get; init; }

    /// <summary>Count of paths trashed successfully.</summary>
    public int SuccessCount => Outcomes.Count(o => o.Succeeded);

    /// <summary>True when at least one path failed for an operational reason.</summary>
    public bool AnyFailed => Outcomes.Any(o => !o.Succeeded);
}

/// <summary>The outcome for a single input path. <see cref="Error"/> is null exactly when the path
/// succeeded; when non-null it is guaranteed non-empty — the constructor rejects a blank reason, so
/// the "failure with no message" state is unrepresentable (which would otherwise count as a failure
/// in <see cref="TrashResult.SuccessCount"/> while printing a blank reason line). Prefer
/// <see cref="Ok"/>/<see cref="Failed"/> at call sites, and read <see cref="Succeeded"/> rather than
/// re-deriving <c>Error is null</c>.</summary>
public sealed record PathOutcome
{
    /// <summary>The input path this outcome describes.</summary>
    public string Path { get; }

    /// <summary>The failure reason, or null on success. Never empty/whitespace when non-null.</summary>
    public string? Error { get; }

    /// <summary>Creates an outcome. A non-null <paramref name="error"/> must be a non-empty reason.</summary>
    /// <exception cref="ArgumentException"><paramref name="error"/> is non-null but blank/whitespace.</exception>
    public PathOutcome(string path, string? error)
    {
        // Single-arg ArgumentException (no paramName) so the message never carries an SR resource key
        // under InvariantGlobalization. This guard is defensive — no producer emits a blank error.
        if (error is not null && string.IsNullOrWhiteSpace(error))
        {
            throw new ArgumentException("A PathOutcome error, when present, must be a non-empty reason.");
        }

        Path = path;
        Error = error;
    }

    /// <summary>True when the path was processed successfully (no error). Annotated so the compiler
    /// knows a non-success outcome has a non-null <see cref="Error"/> (drives null-flow at read sites).</summary>
    [MemberNotNullWhen(false, nameof(Error))]
    public bool Succeeded => Error is null;

    /// <summary>A successful outcome for <paramref name="path"/>.</summary>
    public static PathOutcome Ok(string path) => new(path, null);

    /// <summary>A failed outcome for <paramref name="path"/> with a non-empty <paramref name="error"/>.</summary>
    public static PathOutcome Failed(string path, string error) => new(path, error);
}
