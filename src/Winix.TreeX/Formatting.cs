using System.Globalization;
using System.Text.Json;
using Winix.FileWalk;
using Yort.ShellKit;

namespace Winix.TreeX;

/// <summary>Output formatting for the treex tool — NDJSON lines, JSON summary, and summary line.</summary>
public static class Formatting
{
    /// <summary>
    /// Returns a single NDJSON line (no trailing newline) for <paramref name="node"/>.
    /// Emits per-record fields only: path (relative to root, forward-slash separated),
    /// name, type, size_bytes (null for directories that have no rolled-up size),
    /// modified (ISO 8601), and depth.
    /// </summary>
    /// <remarks>
    /// Tier-2 baseline 2026-05-06 finding F2: pre-fix this method emitted the standard
    /// Winix envelope fields (tool, version, exit_code, exit_reason) on EVERY record.
    /// Per NDJSON convention each line is a node, not a node-plus-envelope; stream-level
    /// metadata belongs in the <c>--json</c> summary (which treex already provides). The
    /// envelope-per-record produced ~35% bandwidth overhead on small trees and ~85KB of
    /// duplicated prefixes on a 1000-record stream.
    ///
    /// Tier-2 baseline 2026-05-06 finding F3: pre-fix the <c>size_bytes</c> field used
    /// the sentinel <c>-1</c> for directories without a rolled-up size. JSON convention
    /// is <c>null</c> for absent values; <c>-1</c> could be misread as a real negative
    /// number by a consumer that doesn't know about the sentinel. Now emits <c>null</c>.
    /// </remarks>
    /// <param name="node">The tree node to serialise.</param>
    /// <param name="depth">Depth of this node relative to the tree root.</param>
    /// <param name="rootPath">Absolute path of the tree root, used to compute relative paths.</param>
    public static string FormatNdjsonLine(TreeNode node, int depth, string rootPath)
    {
        string relativePath = depth == 0
            ? "."
            : Path.GetRelativePath(rootPath, node.FullPath).Replace('\\', '/');

        var (writer, buffer) = JsonHelper.CreateWriter();
        using (writer)
        {
            writer.WriteStartObject();
            writer.WriteString("path", relativePath);
            writer.WriteString("name", node.Name);
            writer.WriteString("type", FormatTypeString(node.Type));
            if (node.SizeBytes < 0)
            {
                writer.WriteNull("size_bytes");
            }
            else
            {
                writer.WriteNumber("size_bytes", node.SizeBytes);
            }
            writer.WriteString("modified", node.Modified.ToString("o", CultureInfo.InvariantCulture));
            writer.WriteNumber("depth", depth);
            writer.WriteEndObject();
        }

        return JsonHelper.GetString(buffer);
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
        var (writer, buffer) = JsonHelper.CreateWriter();
        using (writer)
        {
            writer.WriteStartObject();
            writer.WriteString("tool", toolName);
            writer.WriteString("version", version);
            writer.WriteNumber("exit_code", exitCode);
            writer.WriteString("exit_reason", exitReason);
            writer.WriteNumber("directories", stats.DirectoryCount);
            writer.WriteNumber("files", stats.FileCount);

            if (stats.TotalSizeBytes >= 0)
            {
                writer.WriteNumber("total_size_bytes", stats.TotalSizeBytes);
            }

            writer.WriteEndObject();
        }

        return JsonHelper.GetString(buffer);
    }

    /// <summary>
    /// Returns a JSON error object for failures that occur before any entries are emitted.
    /// Contains the standard Winix envelope fields plus an optional human-readable
    /// <c>error</c> field describing the failure.
    /// </summary>
    /// <param name="exitCode">Process exit code (typically 1 for runtime errors, 125-127 for tool errors).</param>
    /// <param name="exitReason">Machine-readable exit reason string (e.g. "path_not_found", "not_a_directory", "walk_error_partial").</param>
    /// <param name="toolName">Value for the <c>tool</c> envelope field.</param>
    /// <param name="version">Value for the <c>version</c> envelope field.</param>
    /// <param name="errorDetail">
    /// Optional human-readable explanation of the failure (e.g. the same line emitted
    /// to stderr in the non-JSON path). Emitted as a top-level <c>error</c> field when
    /// non-null. Round-2 fresh-eyes 2026-05-09 code-reviewer I1: README + agent-guide +
    /// CHANGELOG documented this field but the prior overload didn't accept or write it,
    /// producing plan-to-code divergence (per
    /// <c>feedback_plan_to_code_divergence_must_be_recorded.md</c>).
    /// </param>
    public static string FormatJsonError(int exitCode, string exitReason, string toolName, string version, string? errorDetail = null)
    {
        var (writer, buffer) = JsonHelper.CreateWriter();
        using (writer)
        {
            writer.WriteStartObject();
            writer.WriteString("tool", toolName);
            writer.WriteString("version", version);
            writer.WriteNumber("exit_code", exitCode);
            writer.WriteString("exit_reason", exitReason);
            if (errorDetail is not null)
            {
                writer.WriteString("error", errorDetail);
            }
            writer.WriteEndObject();
        }

        return JsonHelper.GetString(buffer);
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
        return FileSystemHelper.FormatTypeString(type);
    }

}
