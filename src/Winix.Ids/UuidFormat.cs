namespace Winix.Ids;

/// <summary>Output shape for UUID v4 and v7 identifiers. Orthogonal to case.</summary>
public enum UuidFormat
{
    /// <summary>Hyphenated, e.g. <c>018e7f6f-a7a9-7c3e-8a1b-d0e8f3c94a5d</c>.</summary>
    Default,

    /// <summary>32 hex chars with no hyphens, e.g. <c>018e7f6fa7a97c3e8a1bd0e8f3c94a5d</c>.</summary>
    Hex,

    /// <summary>Brace-wrapped, hyphenated, e.g. <c>{018e7f6f-a7a9-7c3e-8a1b-d0e8f3c94a5d}</c>.</summary>
    Braces,

    /// <summary>URN-prefixed, hyphenated, e.g. <c>urn:uuid:018e7f6f-a7a9-7c3e-8a1b-d0e8f3c94a5d</c>.</summary>
    Urn,
}
