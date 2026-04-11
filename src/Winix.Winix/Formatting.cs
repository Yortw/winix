#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Yort.ShellKit;

namespace Winix.Winix;

/// <summary>
/// Represents the installation status of a single Winix tool.
/// </summary>
public sealed class ToolStatus
{
    /// <summary>Gets the short tool name (e.g. "timeit").</summary>
    public string Name { get; }

    /// <summary>Gets whether the tool is currently installed.</summary>
    public bool IsInstalled { get; }

    /// <summary>Gets the installed version string, or <see langword="null"/> if not installed or unknown.</summary>
    public string? Version { get; }

    /// <summary>Gets the package manager that owns this installation, or <see langword="null"/> if not installed.</summary>
    public string? PackageManager { get; }

    /// <summary>
    /// Initialises a new <see cref="ToolStatus"/>.
    /// </summary>
    /// <param name="name">Short tool name.</param>
    /// <param name="isInstalled">Whether the tool is installed.</param>
    /// <param name="version">Installed version, or <see langword="null"/>.</param>
    /// <param name="packageManager">Owning package manager name, or <see langword="null"/>.</param>
    public ToolStatus(string name, bool isInstalled, string? version, string? packageManager)
    {
        Name = name;
        IsInstalled = isInstalled;
        Version = version;
        PackageManager = packageManager;
    }
}

/// <summary>
/// Produces all human-readable output strings for the winix installer tool.
/// All formatting is kept in this class so that the console entry point stays thin
/// and output can be tested without spawning a process.
/// </summary>
public static class Formatting
{
    private const int DescriptionMaxLength = 50;

    /// <summary>
    /// Formats a single tool install/check result line.
    /// </summary>
    /// <param name="toolName">Short tool name.</param>
    /// <param name="pmName">Package manager that was used.</param>
    /// <param name="success">Whether the operation succeeded.</param>
    /// <param name="error">Error message when <paramref name="success"/> is <see langword="false"/>; otherwise ignored.</param>
    /// <param name="useColor">Whether to emit ANSI colour escape sequences.</param>
    /// <returns>
    /// On success: <c>✓ {toolName} (via {pmName})</c> (green tick when colour enabled).<br/>
    /// On failure: <c>✗ {toolName} (via {pmName}) — {error}</c> (red cross when colour enabled).
    /// </returns>
    public static string FormatToolResult(string toolName, string pmName, bool success, string? error, bool useColor)
    {
        if (success)
        {
            return $"{AnsiColor.Green(useColor)}✓{AnsiColor.Reset(useColor)} {toolName} (via {pmName})";
        }
        else
        {
            return $"{AnsiColor.Red(useColor)}✗{AnsiColor.Reset(useColor)} {toolName} (via {pmName}) — {error}";
        }
    }

    /// <summary>
    /// Formats a one-line status summary such as
    /// <c>"4 of 6 tools installed (3 via winget, 1 via dotnet)"</c>.
    /// </summary>
    /// <param name="statuses">Per-tool status records.</param>
    /// <param name="totalTools">Total number of tools in the suite (denominator).</param>
    /// <returns>A human-readable summary string.</returns>
    public static string FormatStatusSummary(IReadOnlyList<ToolStatus> statuses, int totalTools)
    {
        int installedCount = 0;
        var pmCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var status in statuses)
        {
            if (!status.IsInstalled)
            {
                continue;
            }

            installedCount++;

            if (status.PackageManager != null)
            {
                if (!pmCounts.TryGetValue(status.PackageManager, out int existing))
                {
                    existing = 0;
                }
                pmCounts[status.PackageManager] = existing + 1;
            }
        }

        var sb = new StringBuilder();
        sb.Append(installedCount);
        sb.Append(" of ");
        sb.Append(totalTools);
        sb.Append(" tools installed");

        if (pmCounts.Count > 0)
        {
            // Order by count descending so the dominant PM comes first.
            var ordered = pmCounts.OrderByDescending(kvp => kvp.Value).ToList();

            sb.Append(" (");

            for (int i = 0; i < ordered.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                sb.Append(ordered[i].Value);
                sb.Append(" via ");
                sb.Append(ordered[i].Key);
            }

            sb.Append(')');
        }

        return sb.ToString();
    }

    /// <summary>
    /// Formats a table showing each tool's name, description, installation state, version, and package manager.
    /// Column widths are calculated dynamically to fit the data.
    /// </summary>
    /// <param name="statuses">Per-tool status records.</param>
    /// <param name="descriptions">Map of tool name to description text.</param>
    /// <param name="useColor">Whether to emit ANSI colour escape sequences (reserved for future use).</param>
    /// <returns>A multi-line table string.</returns>
    public static string FormatListTable(IReadOnlyList<ToolStatus> statuses, IReadOnlyDictionary<string, string> descriptions, bool useColor)
    {
        // Column header labels.
        const string ColTool = "Tool";
        const string ColDescription = "Description";
        const string ColInstalled = "Installed";
        const string ColVersion = "Version";
        const string ColVia = "Via";

        // Compute column widths from data, keeping descriptions capped at DescriptionMaxLength.
        int toolWidth = ColTool.Length;
        int descWidth = ColDescription.Length;
        int installedWidth = ColInstalled.Length;
        int versionWidth = ColVersion.Length;
        int viaWidth = ColVia.Length;

        foreach (var status in statuses)
        {
            if (status.Name.Length > toolWidth)
            {
                toolWidth = status.Name.Length;
            }

            if (descriptions.TryGetValue(status.Name, out string? desc) && desc != null)
            {
                int truncated = Math.Min(desc.Length, DescriptionMaxLength);
                if (truncated > descWidth)
                {
                    descWidth = truncated;
                }
            }

            string installedText = status.IsInstalled ? "yes" : "no";
            if (installedText.Length > installedWidth)
            {
                installedWidth = installedText.Length;
            }

            string versionText = status.Version ?? "-";
            if (versionText.Length > versionWidth)
            {
                versionWidth = versionText.Length;
            }

            string viaText = status.PackageManager ?? "-";
            if (viaText.Length > viaWidth)
            {
                viaWidth = viaText.Length;
            }
        }

        var sb = new StringBuilder();

        // Header row.
        sb.Append(ColTool.PadRight(toolWidth));
        sb.Append("  ");
        sb.Append(ColDescription.PadRight(descWidth));
        sb.Append("  ");
        sb.Append(ColInstalled.PadRight(installedWidth));
        sb.Append("  ");
        sb.Append(ColVersion.PadRight(versionWidth));
        sb.Append("  ");
        sb.AppendLine(ColVia);

        // Separator.
        sb.Append(new string('-', toolWidth));
        sb.Append("  ");
        sb.Append(new string('-', descWidth));
        sb.Append("  ");
        sb.Append(new string('-', installedWidth));
        sb.Append("  ");
        sb.Append(new string('-', versionWidth));
        sb.Append("  ");
        sb.AppendLine(new string('-', viaWidth));

        // Data rows.
        foreach (var status in statuses)
        {
            descriptions.TryGetValue(status.Name, out string? rawDesc);
            string descCell = rawDesc != null && rawDesc.Length > DescriptionMaxLength
                ? rawDesc.Substring(0, DescriptionMaxLength)
                : rawDesc ?? string.Empty;

            string installedCell = status.IsInstalled ? "yes" : "no";
            string versionCell = status.Version ?? "-";
            string viaCell = status.PackageManager ?? "-";

            sb.Append(status.Name.PadRight(toolWidth));
            sb.Append("  ");
            sb.Append(descCell.PadRight(descWidth));
            sb.Append("  ");
            sb.Append(installedCell.PadRight(installedWidth));
            sb.Append("  ");
            sb.Append(versionCell.PadRight(versionWidth));
            sb.Append("  ");
            sb.AppendLine(viaCell);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Formats a dry-run preview line showing the command that would be executed.
    /// </summary>
    /// <param name="command">The executable name or path.</param>
    /// <param name="arguments">Arguments that would be passed to the command.</param>
    /// <returns>A string in the form <c>[dry-run] {command} {arg1} {arg2} ...</c>.</returns>
    public static string FormatDryRun(string command, string[] arguments)
    {
        if (arguments.Length == 0)
        {
            return $"[dry-run] {command}";
        }

        return $"[dry-run] {command} {string.Join(" ", arguments)}";
    }

    /// <summary>
    /// Returns a hint message shown when no Winix tools are installed.
    /// </summary>
    /// <returns>A static hint string directing the user to run <c>winix install</c>.</returns>
    public static string FormatNoToolsHint()
    {
        return "No Winix tools installed. Run 'winix install' to install all tools.";
    }
}
