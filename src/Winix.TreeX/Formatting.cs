#nullable enable

using System.Globalization;
using System.Text;
using Winix.FileWalk;

namespace Winix.TreeX;

/// <summary>Output formatting for the treex tool — NDJSON lines, JSON summary, and summary line.</summary>
public static class Formatting
{
    /// <summary>
    /// Returns a single NDJSON line (no trailing newline) for <paramref name="node"/>.
    /// Includes the standard Winix envelope fields (tool, version, exit_code:0, exit_reason:"success")
    /// plus path, name, type, size_bytes, modified (ISO 8601), and depth.
    /// </summary>
    /// <param name="node">The tree node to serialise.</param>
    /// <param name="depth">Depth of this node relative to the tree root.</param>
    /// <param name="toolName">Value for the <c>tool</c> envelope field.</param>
    /// <param name="version">Value for the <c>version</c> envelope field.</param>
    public static string FormatNdjsonLine(TreeNode node, int depth, string toolName, string version)
    {
        var sb = new StringBuilder();
        sb.Append('{');
        sb.AppendFormat(CultureInfo.InvariantCulture,
            "\"tool\":\"{0}\",\"version\":\"{1}\",\"exit_code\":0,\"exit_reason\":\"success\",",
            EscapeJson(toolName),
            EscapeJson(version));

        sb.AppendFormat(CultureInfo.InvariantCulture,
            "\"path\":\"{0}\",\"name\":\"{1}\",\"type\":\"{2}\",\"size_bytes\":{3},",
            EscapeJson(node.FullPath),
            EscapeJson(node.Name),
            EscapeJson(FormatTypeString(node.Type)),
            node.SizeBytes);

        sb.AppendFormat(CultureInfo.InvariantCulture,
            "\"modified\":\"{0}\",\"depth\":{1}",
            EscapeJson(node.Modified.ToString("o", CultureInfo.InvariantCulture)),
            depth);

        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>
    /// Returns a JSON summary object for a completed treex invocation.
    /// Standard envelope fields plus <c>directories</c>, <c>files</c>, and optionally <c>total_size_bytes</c>.
    /// The <c>total_size_bytes</c> field is omitted when <see cref="TreeStats.TotalSizeBytes"/> is negative
    /// (indicating sizes were not computed).
    /// </summary>
    /// <param name="stats">Aggregated tree statistics.</param>
    /// <param name="exitCode">Process exit code.</param>
    /// <param name="exitReason">Machine-readable exit reason string.</param>
    /// <param name="toolName">Value for the <c>tool</c> envelope field.</param>
    /// <param name="version">Value for the <c>version</c> envelope field.</param>
    public static string FormatJsonSummary(
        TreeStats stats,
        int exitCode,
        string exitReason,
        string toolName,
        string version)
    {
        var sb = new StringBuilder();
        sb.AppendFormat(CultureInfo.InvariantCulture,
            "{{\"tool\":\"{0}\",\"version\":\"{1}\",\"exit_code\":{2},\"exit_reason\":\"{3}\",",
            EscapeJson(toolName),
            EscapeJson(version),
            exitCode,
            EscapeJson(exitReason));

        sb.AppendFormat(CultureInfo.InvariantCulture,
            "\"directories\":{0},\"files\":{1}",
            stats.DirectoryCount,
            stats.FileCount);

        if (stats.TotalSizeBytes >= 0)
        {
            sb.AppendFormat(CultureInfo.InvariantCulture,
                ",\"total_size_bytes\":{0}",
                stats.TotalSizeBytes);
        }

        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>
    /// Returns a JSON error object for failures that occur before any entries are emitted.
    /// Contains only the standard Winix envelope fields.
    /// </summary>
    /// <param name="exitCode">Process exit code (typically 125-127 for tool errors).</param>
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
    /// Returns a human-readable summary line like <c>"3 directories, 10 files"</c>.
    /// When <see cref="TreeStats.TotalSizeBytes"/> is non-negative, appends the total size
    /// in parentheses, e.g. <c>"3 directories, 10 files (48.2K)"</c>.
    /// </summary>
    /// <param name="stats">Aggregated tree statistics.</param>
    public static string FormatSummaryLine(TreeStats stats)
    {
        string line = string.Format(CultureInfo.InvariantCulture,
            "{0} {1}, {2} {3}",
            stats.DirectoryCount,
            stats.DirectoryCount == 1 ? "directory" : "directories",
            stats.FileCount,
            stats.FileCount == 1 ? "file" : "files");

        if (stats.TotalSizeBytes >= 0)
        {
            line += $" ({HumanSize.Format(stats.TotalSizeBytes)})";
        }

        return line;
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
