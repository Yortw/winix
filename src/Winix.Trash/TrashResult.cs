using System.Collections.Generic;
using System.Linq;

namespace Winix.Trash;

/// <summary>Outcome of a trash operation over one or more paths.</summary>
public sealed class TrashResult
{
    /// <summary>Per-path outcomes in input order.</summary>
    public required IReadOnlyList<PathOutcome> Outcomes { get; init; }

    /// <summary>Count of paths trashed successfully.</summary>
    public int SuccessCount => Outcomes.Count(o => o.Error is null);

    /// <summary>True when at least one path failed for an operational reason.</summary>
    public bool AnyFailed => Outcomes.Any(o => o.Error is not null);
}

/// <summary>The outcome for a single input path. <see cref="Error"/> is null on success.</summary>
public sealed record PathOutcome(string Path, string? Error);
