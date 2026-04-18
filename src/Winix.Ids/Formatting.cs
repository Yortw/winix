using System;
using System.IO;
using System.Text;
using System.Text.Json;

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
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
        });

        if (uppercase)
        {
            hex = hex.ToUpperInvariant();
        }

        return format == UuidFormat.Urn ? $"urn:uuid:{hex}" : hex;
    }

    /// <summary>
    /// Emits a single JSON object representing a generated identifier, including
    /// type-specific metadata (<c>length</c> and <c>alphabet</c> for NanoID, omitted for others).
    /// The console app wraps multiple elements into an array for <c>--json</c> output.
    /// </summary>
    /// <param name="id">The generated identifier string.</param>
    /// <param name="options">The active options; used to populate type and NanoID metadata fields.</param>
    /// <returns>A UTF-8 JSON object string with at minimum <c>id</c> and <c>type</c> fields.</returns>
    public static string JsonElementFor(string id, IdsOptions options)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("id", id);
            w.WriteString("type", options.Type switch
            {
                IdType.Uuid4  => "uuid4",
                IdType.Uuid7  => "uuid7",
                IdType.Ulid   => "ulid",
                IdType.Nanoid => "nanoid",
                _ => throw new ArgumentOutOfRangeException(nameof(options), options.Type, null),
            });

            if (options.Type == IdType.Nanoid)
            {
                w.WriteNumber("length", options.Length);
                w.WriteString("alphabet", options.Alphabet switch
                {
                    NanoidAlphabet.UrlSafe  => "url-safe",
                    NanoidAlphabet.Alphanum => "alphanum",
                    NanoidAlphabet.Hex      => "hex",
                    NanoidAlphabet.Lower    => "lower",
                    NanoidAlphabet.Upper    => "upper",
                    _ => throw new ArgumentOutOfRangeException(nameof(options), options.Alphabet, null),
                });
            }

            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
