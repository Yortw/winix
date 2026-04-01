using System.Globalization;
using System.Text.Json;
using Yort.ShellKit;

namespace Winix.Squeeze;

/// <summary>
/// Formatting helpers for squeeze results — human-readable stats, JSON output, and byte/duration display.
/// </summary>
public static class Formatting
{
    /// <summary>
    /// Formats a single <see cref="SqueezeResult"/> as a one-line human-readable summary.
    /// Example: <c>filename: 1.0 MB → 512 KB (50.0%, gz, 0.120s)</c>.
    /// Applies green colour to the ratio when reduction exceeds 50%, and dims the filename.
    /// </summary>
    public static string FormatHuman(SqueezeResult result, bool useColor)
    {
        string dim = AnsiColor.Dim(useColor);
        string reset = AnsiColor.Reset(useColor);

        string filename = Path.GetFileName(result.InputPath);
        double ratioPercent = result.Ratio * 100.0;
        string ratioColor = ratioPercent > 50.0 ? AnsiColor.Green(useColor) : "";
        string ratioReset = ratioPercent > 50.0 ? reset : "";

        string shortName = CompressionFormatInfo.GetShortName(result.Format);

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}{1}{2}: {3} \u2192 {4} ({5}{6:F1}%{7}, {8}, {9})",
            dim,
            filename,
            reset,
            DisplayFormat.FormatBytes(result.InputBytes),
            DisplayFormat.FormatBytes(result.OutputBytes),
            ratioColor,
            ratioPercent,
            ratioReset,
            shortName,
            DisplayFormat.FormatDuration(result.Elapsed)
        );
    }

    /// <summary>
    /// Formats one or more <see cref="SqueezeResult"/>s as a JSON object following Winix CLI conventions.
    /// Includes tool/version/exit_code/exit_reason envelope, a files array, and an optional errors array.
    /// When files partially fail, errors are included in the same envelope rather than emitted as
    /// separate JSON objects, so a consumer receives a single parseable document.
    /// </summary>
    public static string FormatJson(
        IReadOnlyList<SqueezeResult> results,
        int exitCode,
        string exitReason,
        string toolName,
        string version,
        IReadOnlyList<string>? errors = null)
    {
        var (writer, buffer) = JsonHelper.CreateWriter();
        using (writer)
        {
            writer.WriteStartObject();
            writer.WriteString("tool", toolName);
            writer.WriteString("version", version);
            writer.WriteNumber("exit_code", exitCode);
            writer.WriteString("exit_reason", exitReason);

            writer.WriteStartArray("files");
            foreach (SqueezeResult r in results)
            {
                writer.WriteStartObject();
                writer.WriteString("input", r.InputPath);
                writer.WriteString("output", r.OutputPath);
                writer.WriteNumber("input_bytes", r.InputBytes);
                writer.WriteNumber("output_bytes", r.OutputBytes);
                JsonHelper.WriteFixedDecimal(writer, "ratio", r.Ratio, 3);
                writer.WriteString("format", CompressionFormatInfo.GetShortName(r.Format));
                JsonHelper.WriteFixedDecimal(writer, "seconds", r.Elapsed.TotalSeconds, 3);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            if (errors is { Count: > 0 })
            {
                writer.WriteStartArray("errors");
                foreach (string error in errors)
                {
                    writer.WriteStringValue(error);
                }
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        return JsonHelper.GetString(buffer);
    }

    /// <summary>
    /// Formats an error as a JSON object following Winix CLI conventions.
    /// Used when squeeze itself fails before processing any files.
    /// </summary>
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
