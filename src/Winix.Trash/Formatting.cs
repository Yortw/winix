#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Winix.Trash;

/// <summary>Renders all user-facing output for the trash tool. Pure: no I/O; accepts and returns strings only.
/// <para>JSON output is hand-built with <see cref="StringBuilder"/> (no System.Text.Json) so it is AOT-clean
/// and consistent with the rest of the Winix suite.</para></summary>
public static class Formatting
{
    private const string DateFormat = "yyyy-MM-ddTHH:mm:ssZ";

    // Column headers for the list table.
    private const string ColName     = "Name";
    private const string ColDeleted  = "Deleted";
    private const string ColOriginal = "Original Path";

    // Width of the Deleted column: "yyyy-MM-ddTHH:mm:ssZ" = 20 chars.
    private const int DeletedWidth = 20;

    // ── Summary messages ──────────────────────────────────────────────────────

    /// <summary>Returns the stderr summary line for a trash operation, e.g. <c>trash: moved 3 item(s) to trash</c>.</summary>
    public static string TrashSummary(int n)
        => $"trash: moved {n} item(s) to trash";

    // ── List table (human-readable) ───────────────────────────────────────────

    /// <summary>Renders a left-aligned table with columns Name / Deleted (UTC ISO-8601) / Original Path
    /// separated by two-space gaps. Each column is padded to the maximum width across all rows (including
    /// the header). Returns an empty string for an empty list.</summary>
    /// <remarks>Column widths are determined dynamically; column order is fixed:
    /// Name | Deleted | Original Path (or <c>—</c> when unknown).</remarks>
    public static string ListTable(IReadOnlyList<TrashedItem> items)
    {
        if (items.Count == 0) { return string.Empty; }

        // Compute max widths.
        int nameW     = ColName.Length;
        int deletedW  = Math.Max(ColDeleted.Length, DeletedWidth);
        int origW     = ColOriginal.Length;

        foreach (TrashedItem item in items)
        {
            if (item.Name.Length > nameW) { nameW = item.Name.Length; }
            string origCell = item.OriginalPath ?? "—";
            if (origCell.Length > origW) { origW = origCell.Length; }
            // Deleted column width is pinned to DeletedWidth (all formatted dates are the same length).
        }

        var sb = new StringBuilder();

        // Header row.
        AppendRow(sb, ColName, ColDeleted, ColOriginal, nameW, deletedW, origW);
        // Separator row.
        AppendRow(sb,
            new string('-', nameW),
            new string('-', deletedW),
            new string('-', origW),
            nameW, deletedW, origW);

        // Data rows.
        foreach (TrashedItem item in items)
        {
            string deletedCell = item.DeletedUtc.HasValue
                ? item.DeletedUtc.Value.ToUniversalTime().ToString(DateFormat, CultureInfo.InvariantCulture)
                : string.Empty;
            string origCell = item.OriginalPath ?? "—";
            AppendRow(sb, item.Name, deletedCell, origCell, nameW, deletedW, origW);
        }

        return sb.ToString();
    }

    // Appends one table row with two-space column separators; each cell is left-padded to its column width.
    // The last column is also padded so every line has the same total length.
    private static void AppendRow(
        StringBuilder sb,
        string col1, string col2, string col3,
        int w1, int w2, int w3)
    {
        sb.Append(col1.PadRight(w1));
        sb.Append("  ");
        sb.Append(col2.PadRight(w2));
        sb.Append("  ");
        sb.Append(col3.PadRight(w3));
        sb.Append('\n');
    }

    // ── ListJson ──────────────────────────────────────────────────────────────

    /// <summary>JSON envelope for <c>--list --json</c>:
    /// <c>{"items":[{"name":"a","original_path":"/x/a","deleted":"2024-01-01T00:00:00Z","size":12,"trash":"home"}]}</c>.
    /// <list type="bullet">
    ///   <item><c>original_path</c> is omitted when null (macOS limitation).</item>
    ///   <item><c>size</c> is omitted when null.</item>
    ///   <item><c>deleted</c> is omitted when null; otherwise always UTC with trailing <c>Z</c>.</item>
    /// </list>
    /// <c>items</c> is always an array, even for 0 or 1 item.</summary>
    public static string ListJson(IReadOnlyList<TrashedItem> items)
    {
        var sb = new StringBuilder("{\"items\":[");
        for (int i = 0; i < items.Count; i++)
        {
            if (i > 0) { sb.Append(','); }
            AppendItemJson(sb, items[i]);
        }
        sb.Append("]}");
        return sb.ToString();
    }

    private static void AppendItemJson(StringBuilder sb, TrashedItem item)
    {
        sb.Append('{');

        sb.Append("\"name\":");
        AppendJsonString(sb, item.Name);

        if (item.OriginalPath is not null)
        {
            sb.Append(",\"original_path\":");
            AppendJsonString(sb, item.OriginalPath);
        }

        if (item.DeletedUtc.HasValue)
        {
            sb.Append(",\"deleted\":");
            AppendJsonString(sb,
                item.DeletedUtc.Value.ToUniversalTime()
                    .ToString(DateFormat, CultureInfo.InvariantCulture));
        }

        if (item.SizeBytes.HasValue)
        {
            sb.Append(",\"size\":");
            sb.Append(item.SizeBytes.Value.ToString(CultureInfo.InvariantCulture));
        }

        sb.Append(",\"trash\":");
        AppendJsonString(sb, item.TrashLocation);

        sb.Append('}');
    }

    // ── EmptyJson ─────────────────────────────────────────────────────────────

    /// <summary>JSON envelope for <c>--empty --json</c>: <c>{"emptied":5,"failed":0}</c>.
    /// <c>emptied</c> is the count of items whose data was confirmed removed (approximate on Windows —
    /// <c>SHEmptyRecycleBinW</c> may affect bins <c>List()</c> does not enumerate, F6); <c>failed</c> is
    /// the count that could not be removed (permission/busy), and a non-zero value drives exit 1.</summary>
    public static string EmptyJson(int emptied, int failed)
        => $"{{\"emptied\":{emptied.ToString(CultureInfo.InvariantCulture)},\"failed\":{failed.ToString(CultureInfo.InvariantCulture)}}}";

    // ── TrashJson ─────────────────────────────────────────────────────────────

    /// <summary>JSON envelope for trash-mode <c>--json</c>:
    /// <c>{"trashed":N,"failed":[{"path":"x","error":"..."}]}</c>.
    /// <c>trashed</c> is the count of paths successfully sent to the bin.
    /// <c>failed</c> lists <see cref="PathOutcome"/> entries with a non-null <see cref="PathOutcome.Error"/>;
    /// it is always an array, empty when all paths succeeded.</summary>
    public static string TrashJson(TrashResult result)
    {
        var sb = new StringBuilder();
        sb.Append("{\"trashed\":");
        sb.Append(result.SuccessCount.ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"failed\":[");

        bool first = true;
        foreach (PathOutcome outcome in result.Outcomes)
        {
            if (outcome.Error is null) { continue; }
            if (!first) { sb.Append(','); }
            first = false;
            sb.Append("{\"path\":");
            AppendJsonString(sb, outcome.Path);
            sb.Append(",\"error\":");
            AppendJsonString(sb, outcome.Error);
            sb.Append('}');
        }

        sb.Append("]}");
        return sb.ToString();
    }

    // ── JSON string escaping (copied from src/Winix.MkSecret/Formatting.cs for suite consistency) ──

    private static void AppendJsonString(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"':  sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n");  break;
                case '\r': sb.Append("\\r");  break;
                case '\t': sb.Append("\\t");  break;
                default:
                    if (c < 0x20)
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        sb.Append('"');
    }
}
