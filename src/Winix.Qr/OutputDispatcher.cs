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
        OutputFormat fmt = options.Format == OutputFormat.Auto
            ? (stdoutIsTty ? OutputFormat.Unicode : OutputFormat.Svg)
            : options.Format;

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
}
