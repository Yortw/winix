using System;

namespace Winix.Ids;

/// <summary>Pure functions composing the final string output for identifiers.</summary>
public static class Formatting
{
    /// <summary>
    /// Formats a GUID according to the requested shape and case.
    /// The URN prefix (<c>urn:uuid:</c>) is always lowercase; only the 32 hex digits
    /// are affected by <paramref name="uppercase"/>.
    /// </summary>
    public static string FormatGuid(Guid guid, UuidFormat format, bool uppercase)
    {
        // Guid.ToString("D")/"N"/"B" produce lowercase hyphenated/hex/brace forms.
        // We upper-case the hex portion in a second pass rather than using "X" formatters,
        // which would also upper-case the "urn:uuid:" prefix (wrong by RFC 9562 §4).
        string hex = guid.ToString(format switch
        {
            UuidFormat.Default => "D",
            UuidFormat.Hex     => "N",
            UuidFormat.Braces  => "B",
            UuidFormat.Urn     => "D",
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        });

        if (uppercase)
        {
            hex = hex.ToUpperInvariant();
        }

        return format == UuidFormat.Urn ? $"urn:uuid:{hex}" : hex;
    }
}
