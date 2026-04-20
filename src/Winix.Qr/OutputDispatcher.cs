#nullable enable
using System;
using Winix.QrCode;
using Winix.QrCode.Renderers;

namespace Winix.Qr;

/// <summary>
/// Routes the combined (payload + <see cref="QrOptions"/>) through the right renderer. Resolves
/// <see cref="OutputFormat.Auto"/> based on whether stdout is a TTY.
/// </summary>
public static class OutputDispatcher
{
    /// <summary>Output payload: either text (unicode/ascii/svg) or bytes (png). Exactly one will be non-null.</summary>
    public readonly record struct Result(string? Text, byte[]? Bytes);

    /// <summary>Render the payload.</summary>
    /// <param name="payload">The encoded string (from <see cref="PayloadBuilder"/>).</param>
    /// <param name="options">Parsed CLI options.</param>
    /// <param name="stdoutIsTty">Whether stdout is a terminal (drives <see cref="OutputFormat.Auto"/> resolution).</param>
    public static Result Dispatch(string payload, QrOptions options, bool stdoutIsTty)
    {
        // Auto-format resolution order:
        //   1. --output PATH with a recognised extension (.png/.svg) → respect extension.
        //      Users supplying a .png filename want a PNG; serving SVG bytes would mislead tools
        //      that route on extension.
        //   2. stdout is a TTY → unicode half-block art.
        //   3. stdout is redirected/piped → SVG (text, pipe-safe).
        OutputFormat fmt = options.Format != OutputFormat.Auto
            ? options.Format
            : ResolveAutoFormat(options.OutputPath, stdoutIsTty);

        bool drawQuietZone = !options.NoMargin;

        if (fmt == OutputFormat.Png)
        {
            byte[] bytes = PngRenderer.Render(payload, options.Ecc, options.PixelsPerModule, drawQuietZone);
            return new Result(null, bytes);
        }

        QrMatrix matrix = QrEncoder.Encode(payload, options.Ecc);
        string text = fmt switch
        {
            OutputFormat.Unicode => UnicodeRenderer.Render(matrix, drawQuietZone),
            OutputFormat.Ascii   => AsciiRenderer.Render(matrix, drawQuietZone),
            OutputFormat.Svg     => SvgRenderer.Render(matrix, options.PixelsPerModule, drawQuietZone),
            _ => throw new InvalidOperationException($"Unhandled format {fmt}"),
        };
        return new Result(text, null);
    }

    private static OutputFormat ResolveAutoFormat(string? outputPath, bool stdoutIsTty)
    {
        if (!string.IsNullOrEmpty(outputPath))
        {
            string ext = System.IO.Path.GetExtension(outputPath);
            if (string.Equals(ext, ".png", StringComparison.OrdinalIgnoreCase)) return OutputFormat.Png;
            if (string.Equals(ext, ".svg", StringComparison.OrdinalIgnoreCase)) return OutputFormat.Svg;
            // Unknown extension: fall through to stream-based default (safer than assuming).
        }
        return stdoutIsTty ? OutputFormat.Unicode : OutputFormat.Svg;
    }
}
