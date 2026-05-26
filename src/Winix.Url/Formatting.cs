#nullable enable
using System;
using System.Globalization;
using System.Text;
using Yort.ShellKit;

namespace Winix.Url;

/// <summary>Output composition for <c>url parse</c>. Pure — no I/O.</summary>
public static class Formatting
{
    /// <summary>Plain-text key=value lines. Null fields omitted. Query always emitted as raw form-encoded string.</summary>
    public static string PlainText(ParsedUrl p)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(p.Scheme)) sb.Append("scheme=").Append(p.Scheme).Append('\n');
        if (p.UserInfo is not null) sb.Append("userinfo=").Append(p.UserInfo).Append('\n');
        if (!string.IsNullOrEmpty(p.Host)) sb.Append("host=").Append(p.Host).Append('\n');
        if (p.Port is int port) sb.Append("port=").Append(port.ToString(CultureInfo.InvariantCulture)).Append('\n');
        if (!string.IsNullOrEmpty(p.Path)) sb.Append("path=").Append(p.Path).Append('\n');
        if (!string.IsNullOrEmpty(p.RawQuery)) sb.Append("query=").Append(p.RawQuery).Append('\n');
        if (p.Fragment is not null) sb.Append("fragment=").Append(p.Fragment).Append('\n');
        // Trim trailing newline so the output round-trips cleanly via Console.WriteLine.
        if (sb.Length > 0 && sb[sb.Length - 1] == '\n')
        {
            sb.Length--;
        }
        return sb.ToString();
    }

    /// <summary>Extract a single named field as a string. Unknown names throw.</summary>
    /// <remarks>
    /// The <c>query</c> field returns the URL's original query string (percent-escapes preserved) —
    /// faithful to the input, not form-encoded re-serialisation. Use <c>--json</c> if you need
    /// the decoded key/value pairs.
    /// </remarks>
    public static string Field(ParsedUrl p, string name) => name switch
    {
        "scheme"   => p.Scheme,
        "userinfo" => p.UserInfo ?? "",
        "host"     => p.Host,
        "port"     => p.Port?.ToString(CultureInfo.InvariantCulture) ?? "",
        "path"     => p.Path,
        "query"    => p.RawQuery,
        "fragment" => p.Fragment ?? "",
        _ => throw new ArgumentException($"unknown field '{name}' (expected: scheme, userinfo, host, port, path, query, fragment)"),
    };

    /// <summary>Structured JSON; query is array-of-objects preserving order and duplicates.</summary>
    public static string Json(ParsedUrl p)
    {
        var (writer, buffer) = JsonHelper.CreateWriter();
        using (writer)
        {
            writer.WriteStartObject();
            writer.WriteString("scheme", p.Scheme);
            if (p.UserInfo is null) writer.WriteNull("userinfo"); else writer.WriteString("userinfo", p.UserInfo);
            writer.WriteString("host", p.Host);
            if (p.Port is int port) writer.WriteNumber("port", port); else writer.WriteNull("port");
            writer.WriteString("path", p.Path);
            writer.WriteStartArray("query");
            foreach (var (k, v) in p.QueryPairs)
            {
                writer.WriteStartObject();
                writer.WriteString("key", k);
                writer.WriteString("value", v);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            if (p.Fragment is null) writer.WriteNull("fragment"); else writer.WriteString("fragment", p.Fragment);
            writer.WriteEndObject();
        }
        return JsonHelper.GetString(buffer);
    }
}
