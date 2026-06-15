using System;
using System.Globalization;

namespace Winix.ProcessSupervision;

/// <summary>
/// Maps the small set of signal names <c>runfor --signal</c> accepts (HUP/INT/QUIT/KILL/TERM, with
/// an optional <c>SIG</c> prefix) to their numbers, and back. v1 accepts names only, not numbers,
/// to keep the surface tight and the help finite.
/// </summary>
public static class UnixSignal
{
    /// <summary>SIGTERM (15) — the default deadline signal.</summary>
    public const int DefaultSignal = 15;

    /// <summary>
    /// Parses a signal name (case-insensitive, optional <c>SIG</c> prefix) into its number.
    /// </summary>
    /// <param name="name">The signal name to parse, e.g. <c>"TERM"</c>, <c>"sigterm"</c>.</param>
    /// <param name="signal">Receives the signal number on success; 0 on failure.</param>
    /// <returns><c>true</c> and sets <paramref name="signal"/> for a known name; otherwise <c>false</c>
    /// and <paramref name="signal"/> is 0.</returns>
    public static bool TryParse(string name, out int signal)
    {
        signal = 0;
        if (string.IsNullOrWhiteSpace(name)) { return false; }

        string n = name.Trim().ToUpperInvariant();
        if (n.StartsWith("SIG", StringComparison.Ordinal)) { n = n.Substring(3); }

        switch (n)
        {
            case "HUP":  signal = NativeProcess.SigHup;  return true;
            case "INT":  signal = NativeProcess.SigInt;  return true;
            case "QUIT": signal = NativeProcess.SigQuit; return true;
            case "KILL": signal = NativeProcess.SigKill; return true;
            case "TERM": signal = NativeProcess.SigTerm; return true;
            default: return false;
        }
    }

    /// <summary>
    /// Returns the canonical name for a known signal number, or the number formatted as a string
    /// for unknown values.
    /// </summary>
    /// <param name="signal">The signal number.</param>
    /// <returns>E.g. <c>"TERM"</c> for 15, <c>"KILL"</c> for 9.</returns>
    public static string ToName(int signal)
    {
        switch (signal)
        {
            case NativeProcess.SigHup:  return "HUP";
            case NativeProcess.SigInt:  return "INT";
            case NativeProcess.SigQuit: return "QUIT";
            case NativeProcess.SigKill: return "KILL";
            case NativeProcess.SigTerm: return "TERM";
            default: return signal.ToString(CultureInfo.InvariantCulture);
        }
    }
}
