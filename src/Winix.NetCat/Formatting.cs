#nullable enable

using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Yort.ShellKit;

namespace Winix.NetCat;

/// <summary>
/// Pure formatting helpers. No I/O — the console app writes the returned strings.
/// </summary>
public static class Formatting
{
    /// <summary>Returns a single line "PORT open", optionally ANSI-coloured green.</summary>
    public static string FormatOpenPortLine(int port, bool useColor)
        => Wrap($"{port.ToString(CultureInfo.InvariantCulture)} open", AnsiColor.Green(useColor), useColor);

    /// <summary>Returns a single line "PORT closed", optionally ANSI-coloured red.</summary>
    public static string FormatClosedPortLine(int port, bool useColor)
        => Wrap($"{port.ToString(CultureInfo.InvariantCulture)} closed", AnsiColor.Red(useColor), useColor);

    /// <summary>Returns a single line "PORT timeout", optionally ANSI-coloured yellow.</summary>
    public static string FormatTimeoutPortLine(int port, bool useColor)
        => Wrap($"{port.ToString(CultureInfo.InvariantCulture)} timeout", AnsiColor.Yellow(useColor), useColor);

    private static string Wrap(string text, string colorOpen, bool useColor)
        => useColor ? colorOpen + text + AnsiColor.Reset(true) : text;
}
