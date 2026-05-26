#nullable enable
using System;
using System.Collections.Generic;
using System.Text;

namespace Winix.Qr.Helpers;

/// <summary>Builds a <c>mailto:</c> URI per RFC 6068.</summary>
public static class MailtoPayload
{
    /// <summary>Build the mailto URI.</summary>
    /// <exception cref="ArgumentException"><paramref name="to"/> is empty.</exception>
    public static string Build(string to, string? subject, string? body, string? cc, string? bcc)
    {
        if (string.IsNullOrEmpty(to))
        {
            // Round-2 review CR-I1: single-arg ArgumentException avoids the
            // InvariantGlobalization-induced 'Arg_ParamName_Name' suffix in qr.csproj's AOT build.
            throw new ArgumentException("--to must be non-empty.");
        }

        StringBuilder sb = new();
        sb.Append("mailto:");
        sb.Append(to);

        List<string> parts = new();
        if (!string.IsNullOrEmpty(subject)) parts.Add($"subject={Uri.EscapeDataString(subject)}");
        if (!string.IsNullOrEmpty(body))    parts.Add($"body={Uri.EscapeDataString(body)}");
        if (!string.IsNullOrEmpty(cc))      parts.Add($"cc={Uri.EscapeDataString(cc)}");
        if (!string.IsNullOrEmpty(bcc))     parts.Add($"bcc={Uri.EscapeDataString(bcc)}");

        if (parts.Count > 0)
        {
            sb.Append('?');
            sb.Append(string.Join('&', parts));
        }
        return sb.ToString();
    }
}
