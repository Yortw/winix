using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
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
    /// Emits per-record fields only: path, name, type, size_bytes (null for directory
    /// entries that don't carry a rolled-up size), modified (ISO 8601 with offset, or
    /// null for entries without a populated mtime), depth, and is_text (only when non-null).
    /// </summary>
    /// <remarks>
    /// Tier-2 baseline 2026-05-06 finding F2: pre-fix this method emitted the standard
    /// Winix envelope fields (tool, version, exit_code, exit_reason) on EVERY record.
    /// Per NDJSON convention each line is a record, not a record-plus-envelope; stream-
    /// level metadata belongs in the <c>--json</c> summary which files already provides.
    /// Bandwidth cost on a 1000-file scan was ~80KB of redundant prefix.
    ///
    /// Tier-2 baseline 2026-05-06 finding F3: pre-fix directory entries serialised
    /// <c>"size_bytes":-1</c> (the no-rollup sentinel) and
    /// <c>"modified":"0001-01-01T00:00:00.0000000+00:00"</c> (DateTime.MinValue ToString
    /// emitted as a real timestamp). Both are JSON anti-patterns — consumers parsing
    /// these into typed values would get either a real negative number or a year-1
    /// date. Now both emit <c>null</c> when unpopulated.
    /// </remarks>
    /// <param name="entry">The file system entry to serialise.</param>
    public static string FormatNdjsonLine(FileEntry entry)
    {
        var (writer, buffer) = JsonHelper.CreateWriter();
        using (writer)
        {
            writer.WriteStartObject();
            writer.WriteString("path", entry.Path);
            writer.WriteString("name", entry.Name);
            writer.WriteString("type", FormatTypeString(entry.Type));

            if (entry.SizeBytes < 0)
            {
                writer.WriteNull("size_bytes");
            }
            else
            {
                writer.WriteNumber("size_bytes", entry.SizeBytes);
            }

            if (entry.Modified == default)
            {
                writer.WriteNull("modified");
            }
            else
            {
                writer.WriteString("modified", entry.Modified.ToString("o", CultureInfo.InvariantCulture));
            }

            writer.WriteNumber("depth", entry.Depth);

            if (entry.IsText.HasValue)
            {
                writer.WriteBoolean("is_text", entry.IsText.Value);
            }

            writer.WriteEndObject();
        }

        return JsonHelper.GetString(buffer);
    }

    /// <summary>
    /// Returns a JSON summary object for a completed files invocation.
    /// Standard envelope fields plus <c>count</c>, <c>searched_roots</c>, and a
    /// <c>walk_errors</c> array enumerating any directories or files that could not
    /// be read.
    /// </summary>
    /// <param name="count">Number of entries emitted.</param>
    /// <param name="searchedRoots">Root paths that were walked.</param>
    /// <param name="exitCode">Process exit code.</param>
    /// <param name="exitReason">Machine-readable exit reason string.</param>
    /// <param name="toolName">Value for the <c>tool</c> envelope field.</param>
    /// <param name="version">Value for the <c>version</c> envelope field.</param>
    /// <param name="walkErrors">
    /// Walk errors aggregated across all roots. Always emitted (empty array on success)
    /// so machine consumers can use a single shape regardless of outcome. Round-1
    /// fresh-eyes 2026-05-09 silent-failure-hunter C1: pre-fix, walk failures were
    /// silently swallowed and the JSON envelope had no signal that paths were skipped.
    /// </param>
    public static string FormatJsonSummary(
        int count,
        IReadOnlyList<string> searchedRoots,
        int exitCode,
        string exitReason,
        string toolName,
        string version,
        IReadOnlyList<WalkError>? walkErrors = null)
    {
        var (writer, buffer) = JsonHelper.CreateWriter();
        using (writer)
        {
            writer.WriteStartObject();
            writer.WriteString("tool", toolName);
            writer.WriteString("version", version);
            writer.WriteNumber("exit_code", exitCode);
            writer.WriteString("exit_reason", exitReason);
            writer.WriteNumber("count", count);
            writer.WriteStartArray("searched_roots");
            foreach (string root in searchedRoots)
            {
                writer.WriteStringValue(root);
            }
            writer.WriteEndArray();
            writer.WriteStartArray("walk_errors");
            if (walkErrors is not null)
            {
                foreach (WalkError walkError in walkErrors)
                {
                    writer.WriteStartObject();
                    writer.WriteString("path", walkError.Path);
                    writer.WriteString("reason", walkError.Reason);
                    writer.WriteEndObject();
                }
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return JsonHelper.GetString(buffer);
    }

    /// <summary>
    /// Returns a JSON error object for failures that occur before any entries are emitted.
    /// Contains the standard Winix envelope fields, an optional <c>error</c> string for
    /// the human-readable detail, an empty <c>searched_roots</c> array, and an empty
    /// <c>walk_errors</c> array — both for shape parity with <see cref="FormatJsonSummary"/>.
    /// </summary>
    /// <param name="exitCode">Process exit code (typically 1 for runtime errors, 125-127 for tool errors).</param>
    /// <param name="exitReason">Machine-readable exit reason string.</param>
    /// <param name="toolName">Value for the <c>tool</c> envelope field.</param>
    /// <param name="version">Value for the <c>version</c> envelope field.</param>
    /// <param name="errorDetail">
    /// Optional human-readable explanation of the failure. Round-1 fresh-eyes 2026-05-09
    /// code-reviewer I3 + <c>feedback_dual_envelope_formatters_drift.md</c>: the pre-walk
    /// envelope previously omitted both the human reason and the array fields, leaving
    /// JSON consumers to special-case two shapes. Now both envelopes carry
    /// <c>searched_roots</c> + <c>walk_errors</c> (empty on this path) and the error
    /// envelope additionally carries <c>error</c>.
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
            // Round-1 fresh-eyes 2026-05-09: shape parity with FormatJsonSummary so
            // machine consumers can use a single shape (empty arrays here, populated
            // there). Avoids the dual-envelope-formatter shape divergence class.
            writer.WriteStartArray("searched_roots");
            writer.WriteEndArray();
            writer.WriteStartArray("walk_errors");
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return JsonHelper.GetString(buffer);
    }

    /// <summary>
    /// Returns the display string for a <see cref="FileEntryType"/>: <c>file</c>, <c>dir</c>, or <c>link</c>.
    /// </summary>
    public static string FormatTypeString(FileEntryType type)
    {
        return FileSystemHelper.FormatTypeString(type);
    }

}
