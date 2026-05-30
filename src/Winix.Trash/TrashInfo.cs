#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Winix.Trash;

/// <summary>Parsed contents of a FreeDesktop <c>.trashinfo</c> file.</summary>
/// <param name="OriginalPath">Decoded absolute original path.</param>
/// <param name="DeletionLocal">Deletion timestamp as written — local wall-clock with no timezone. Its
/// <see cref="DateTime.Kind"/> is <see cref="DateTimeKind.Unspecified"/> (NOT Local), because the spec
/// stores no offset. Callers needing UTC must <c>DateTime.SpecifyKind(value, DateTimeKind.Local)</c>
/// first — calling <c>ToUniversalTime()</c> directly on an Unspecified value is a host-timezone
/// footgun.</param>
public sealed record TrashInfoRecord(string OriginalPath, DateTime DeletionLocal);

/// <summary>Reads and writes FreeDesktop <c>.trashinfo</c> files. Path values are percent-encoded
/// per RFC 2396 (every byte except RFC-2396 unreserved chars and the path separator <c>/</c>);
/// DeletionDate is local time formatted <c>yyyy-MM-ddTHH:mm:ss</c> with no timezone suffix.</summary>
public static class TrashInfo
{
    // RFC 2396 unreserved set, plus '/' which the spec keeps literal in Path.
    private const string Unreserved =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_.!~*'()/";

    /// <summary>Serialises a <c>.trashinfo</c> body (LF line endings, trailing newline).</summary>
    public static string Write(string originalPath, DateTime deletionLocal)
    {
        var sb = new StringBuilder("[Trash Info]\n");
        sb.Append("Path=").Append(Encode(originalPath)).Append('\n');
        sb.Append("DeletionDate=")
          .Append(deletionLocal.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture))
          .Append('\n');
        return sb.ToString();
    }

    /// <summary>Parses a <c>.trashinfo</c> body, or null if Path/DeletionDate are missing/invalid.</summary>
    public static TrashInfoRecord? Parse(string body)
    {
        string? path = null;
        DateTime? date = null;
        foreach (string raw in body.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.StartsWith("Path=", StringComparison.Ordinal))
            {
                path = Decode(line.Substring("Path=".Length));
            }
            else if (line.StartsWith("DeletionDate=", StringComparison.Ordinal))
            {
                if (DateTime.TryParseExact(line.Substring("DeletionDate=".Length),
                        "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out DateTime d))
                {
                    date = d;
                }
            }
        }
        if (path is null || date is null) { return null; }
        return new TrashInfoRecord(path, date.Value);
    }

    private static string Encode(string s)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(s);
        var sb = new StringBuilder(bytes.Length);
        foreach (byte b in bytes)
        {
            char c = (char)b;
            if (b < 0x80 && Unreserved.IndexOf(c) >= 0) { sb.Append(c); }
            else { sb.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture)); }
        }
        return sb.ToString();
    }

    private static string Decode(string s)
    {
        // Defensive (F12): a trailing bare '%', a single-hex-digit '%A', or a non-hex pair '%ZZ' must
        // NOT throw — a corrupt .trashinfo must not crash --list. Emit the '%' literally in those cases.
        var bytes = new List<byte>(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '%' && i + 2 < s.Length
                && Uri.IsHexDigit(s[i + 1]) && Uri.IsHexDigit(s[i + 2]))
            {
                bytes.Add((byte)((Uri.FromHex(s[i + 1]) << 4) | Uri.FromHex(s[i + 2])));
                i += 2;
            }
            else { bytes.Add((byte)s[i]); }
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }
}
