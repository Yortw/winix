#nullable enable

using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Yort.ShellKit;

namespace Winix.WhoHolds;

/// <summary>
/// Output formatting for the whoholds tool — table, PID-only, JSON results, and diagnostic messages.
/// All methods are pure (no I/O); the console app is responsible for writing the returned strings.
/// </summary>
public static class Formatting
{
    private const string HeaderPid      = "PID";
    private const string HeaderProcess  = "Process";
    private const string HeaderResource = "Resource";

    /// <summary>
    /// Returns a formatted table string listing each result with columns: PID, Process, Resource.
    /// Column widths are derived from the widest value in each column.
    /// The header row is dimmed when <paramref name="useColor"/> is <see langword="true"/>.
    /// </summary>
    /// <param name="results">The lock results to display. Must not be null.</param>
    /// <param name="useColor">When <see langword="true"/>, ANSI colour codes are included.</param>
    public static string FormatTable(IReadOnlyList<LockInfo> results, bool useColor)
    {
        // Compute column widths from data, floored at header widths.
        int pidWidth      = HeaderPid.Length;
        int processWidth  = HeaderProcess.Length;
        int resourceWidth = HeaderResource.Length;

        foreach (LockInfo item in results)
        {
            int pidLen = item.ProcessId.ToString(CultureInfo.InvariantCulture).Length;
            if (pidLen > pidWidth) { pidWidth = pidLen; }
            if (item.ProcessName.Length > processWidth) { processWidth = item.ProcessName.Length; }
            if (item.Resource.Length > resourceWidth) { resourceWidth = item.Resource.Length; }
        }

        string dim   = AnsiColor.Dim(useColor);
        string reset = AnsiColor.Reset(useColor);

        var sb = new StringBuilder();

        // Header row
        sb.Append(dim);
        sb.Append(HeaderPid.PadRight(pidWidth));
        sb.Append("  ");
        sb.Append(HeaderProcess.PadRight(processWidth));
        sb.Append("  ");
        sb.Append(HeaderResource);
        sb.Append(reset);
        sb.AppendLine();

        // Data rows
        foreach (LockInfo item in results)
        {
            string pid = item.ProcessId.ToString(CultureInfo.InvariantCulture);
            sb.Append(pid.PadRight(pidWidth));
            sb.Append("  ");
            sb.Append(item.ProcessName.PadRight(processWidth));
            sb.Append("  ");
            sb.AppendLine(item.Resource);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Returns one PID per line (no trailing whitespace), suitable for scripting.
    /// </summary>
    /// <param name="results">The lock results whose PIDs should be emitted. Must not be null.</param>
    public static string FormatPidOnly(IReadOnlyList<LockInfo> results)
    {
        var sb = new StringBuilder();
        foreach (LockInfo item in results)
        {
            sb.AppendLine(item.ProcessId.ToString(CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Returns a warning message indicating that the tool is running without elevation
    /// and results may be incomplete. Coloured yellow with a warning symbol when
    /// <paramref name="useColor"/> is <see langword="true"/>.
    /// </summary>
    /// <param name="useColor">When <see langword="true"/>, ANSI colour codes are included.</param>
    public static string FormatElevationWarning(bool useColor)
    {
        string yellow = AnsiColor.Yellow(useColor);
        string reset  = AnsiColor.Reset(useColor);
        return $"{yellow}Warning: Not elevated — only showing current user's processes.{reset}";
    }

    /// <summary>
    /// Returns a plain-text message indicating that no processes were found holding
    /// <paramref name="resource"/>.
    /// </summary>
    /// <param name="resource">The queried resource (file path or port specifier).</param>
    public static string FormatNoResults(string resource)
    {
        return $"No processes found holding {resource}";
    }

    /// <summary>
    /// Returns a JSON object containing the standard Winix envelope fields and a
    /// <c>processes</c> array with one object per <see cref="LockInfo"/> entry.
    /// Each process object has <c>pid</c>, <c>name</c>, and <c>resource</c> fields.
    /// </summary>
    /// <param name="results">The lock results to serialise. Must not be null.</param>
    /// <param name="exitCode">Process exit code written into the envelope.</param>
    /// <param name="exitReason">Machine-readable exit reason string.</param>
    /// <param name="toolName">Value for the <c>tool</c> envelope field.</param>
    /// <param name="version">Value for the <c>version</c> envelope field.</param>
    public static string FormatJson(
        IReadOnlyList<LockInfo> results,
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
            writer.WriteStartArray("processes");
            foreach (LockInfo item in results)
            {
                writer.WriteStartObject();
                writer.WriteNumber("pid", item.ProcessId);
                writer.WriteString("name", item.ProcessName);
                writer.WriteString("resource", item.Resource);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return JsonHelper.GetString(buffer);
    }

    /// <summary>
    /// Returns a JSON error object containing only the standard Winix envelope fields.
    /// Used for failures that occur before any results are available.
    /// </summary>
    /// <param name="exitCode">Process exit code (typically 125–127 for tool errors).</param>
    /// <param name="exitReason">Machine-readable exit reason string.</param>
    /// <param name="toolName">Value for the <c>tool</c> envelope field.</param>
    /// <param name="version">Value for the <c>version</c> envelope field.</param>
    public static string FormatJsonError(int exitCode, string exitReason, string toolName, string version)
    {
        var (writer, buffer) = JsonHelper.CreateWriter();
        using (writer)
        {
            writer.WriteStartObject();
            writer.WriteString("tool", toolName);
            writer.WriteString("version", version);
            writer.WriteNumber("exit_code", exitCode);
            writer.WriteString("exit_reason", exitReason);
            writer.WriteEndObject();
        }

        return JsonHelper.GetString(buffer);
    }
}
