#nullable enable

using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Winix.FileWalk;
using Yort.ShellKit;

namespace Winix.Files;

/// <summary>Output formatting for the files tool — paths, long listing, NDJSON, and JSON summary.</summary>
public static class Formatting
{
    /// <summary>
    /// Returns the path of <paramref name="entry"/>, coloured by type when enabled.
    /// Directories are blue, symlinks are cyan, files are uncoloured.
    /// </summary>
    public static string FormatPath(FileEntry entry, bool useColor = false)
    {
        if (!useColor)
        {
            return entry.Path;
        }

        string color = entry.Type switch
        {
            FileEntryType.Directory => AnsiColor.Blue(true),
            FileEntryType.Symlink => AnsiColor.Cyan(true),
            _ => ""
        };

        if (color.Length == 0)
        {
            return entry.Path;
        }

        return $"{color}{entry.Path}{AnsiColor.Reset(true)}";
    }

    /// <summary>
    /// Returns a tab-delimited long-format line for <paramref name="entry"/>.
    /// Fields: path (coloured by type when enabled), size (bytes with commas, or <c>-</c> for directories),
    /// modified (local time yyyy-MM-dd HH:mm, or <c>-</c>), type string.
    /// </summary>
    public static string FormatLong(FileEntry entry, bool useColor = false)
    {
        string size = entry.Type == FileEntryType.Directory
            ? "-"
            : entry.SizeBytes.ToString("N0", CultureInfo.InvariantCulture);

        string modified = entry.Modified == default
            ? "-"
            : entry.Modified.LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

        string type = FormatTypeString(entry.Type);
        string path = FormatPath(entry, useColor);

        return $"{path}\t{size}\t{modified}\t{type}";
    }

    /// <summary>
    /// Returns a single NDJSON line (no trailing newline) for <paramref name="entry"/>.
    /// Includes the standard Winix envelope fields (tool, version, exit_code:0, exit_reason:"success")
    /// plus all <see cref="FileEntry"/> fields. The <c>is_text</c> field is only present when non-null.
    /// The <c>modified</c> field is ISO 8601 with offset.
    /// </summary>
    /// <param name="entry">The file system entry to serialise.</param>
    /// <param name="toolName">Value for the <c>tool</c> envelope field.</param>
    /// <param name="version">Value for the <c>version</c> envelope field.</param>
    public static string FormatNdjsonLine(FileEntry entry, string toolName, string version)
    {
        var sb = new StringBuilder();
        sb.Append('{');
        sb.AppendFormat(CultureInfo.InvariantCulture,
            "\"tool\":\"{0}\",\"version\":\"{1}\",\"exit_code\":0,\"exit_reason\":\"success\",",
            EscapeJson(toolName),
            EscapeJson(version));

        sb.AppendFormat(CultureInfo.InvariantCulture,
            "\"path\":\"{0}\",\"name\":\"{1}\",\"type\":\"{2}\",\"size_bytes\":{3},",
            EscapeJson(entry.Path),
            EscapeJson(entry.Name),
            EscapeJson(FormatTypeString(entry.Type)),
            entry.SizeBytes);

        sb.AppendFormat(CultureInfo.InvariantCulture,
            "\"modified\":\"{0}\",\"depth\":{1}",
            EscapeJson(entry.Modified.ToString("o", CultureInfo.InvariantCulture)),
            entry.Depth);

        if (entry.IsText.HasValue)
        {
            sb.AppendFormat(CultureInfo.InvariantCulture,
                ",\"is_text\":{0}",
                entry.IsText.Value ? "true" : "false");
        }

        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>
    /// Returns a JSON summary object for a completed files invocation.
    /// Standard envelope fields plus <c>count</c> and <c>searched_roots</c>.
    /// </summary>
    /// <param name="count">Number of entries emitted.</param>
    /// <param name="searchedRoots">Root paths that were walked.</param>
    /// <param name="exitCode">Process exit code.</param>
    /// <param name="exitReason">Machine-readable exit reason string.</param>
    /// <param name="toolName">Value for the <c>tool</c> envelope field.</param>
    /// <param name="version">Value for the <c>version</c> envelope field.</param>
    public static string FormatJsonSummary(
        int count,
        IReadOnlyList<string> searchedRoots,
        int exitCode,
        string exitReason,
        string toolName,
        string version)
    {
        var sb = new StringBuilder();
        sb.AppendFormat(CultureInfo.InvariantCulture,
            "{{\"tool\":\"{0}\",\"version\":\"{1}\",\"exit_code\":{2},\"exit_reason\":\"{3}\",\"count\":{4},\"searched_roots\":[",
            EscapeJson(toolName),
            EscapeJson(version),
            exitCode,
            EscapeJson(exitReason),
            count);

        for (int i = 0; i < searchedRoots.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            sb.AppendFormat(CultureInfo.InvariantCulture, "\"{0}\"", EscapeJson(searchedRoots[i]));
        }

        sb.Append("]}");
        return sb.ToString();
    }

    /// <summary>
    /// Returns a JSON error object for failures that occur before any entries are emitted.
    /// Contains only the standard Winix envelope fields.
    /// </summary>
    /// <param name="exitCode">Process exit code (typically 125–127 for tool errors).</param>
    /// <param name="exitReason">Machine-readable exit reason string.</param>
    /// <param name="toolName">Value for the <c>tool</c> envelope field.</param>
    /// <param name="version">Value for the <c>version</c> envelope field.</param>
    public static string FormatJsonError(int exitCode, string exitReason, string toolName, string version)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{{\"tool\":\"{0}\",\"version\":\"{1}\",\"exit_code\":{2},\"exit_reason\":\"{3}\"}}",
            EscapeJson(toolName),
            EscapeJson(version),
            exitCode,
            EscapeJson(exitReason));
    }

    /// <summary>
    /// Returns the display string for a <see cref="FileEntryType"/>: <c>file</c>, <c>dir</c>, or <c>link</c>.
    /// </summary>
    public static string FormatTypeString(FileEntryType type)
    {
        return type switch
        {
            FileEntryType.File => "file",
            FileEntryType.Directory => "dir",
            FileEntryType.Symlink => "link",
            _ => "file"
        };
    }

    /// <summary>
    /// Escapes characters that are not safe inside a JSON string value:
    /// backslash, double-quote, carriage return, newline, and tab.
    /// </summary>
    private static string EscapeJson(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n")
            .Replace("\t", "\\t");
    }
}
