namespace Winix.Clip;

/// <summary>
/// Describes one helper configuration: which binary to run for copy, paste, and clear,
/// plus arg vectors. <see cref="HelperSets"/> exposes predefined sets for each OS.
/// </summary>
/// <param name="Name">Human-readable identifier (used in error messages).</param>
/// <param name="CopyBinary">Binary name for copy.</param>
/// <param name="CopyArgs">Arg vector for copy (stdin carries the payload).</param>
/// <param name="PasteBinary">Binary name for paste.</param>
/// <param name="PasteArgs">Arg vector for paste (stdout carries the result).</param>
/// <param name="ClearBinary">Binary name for clear.</param>
/// <param name="ClearArgs">Arg vector for clear.</param>
/// <param name="ClearUsesEmptyStdin">
/// True if clear is implemented as "copy with empty stdin" (xclip, pbcopy). False if the
/// helper has a dedicated clear command (wl-copy --clear, xsel --clear).
/// </param>
public sealed record ClipboardHelperSet(
    string Name,
    string CopyBinary,
    IReadOnlyList<string> CopyArgs,
    string PasteBinary,
    IReadOnlyList<string> PasteArgs,
    string ClearBinary,
    IReadOnlyList<string> ClearArgs,
    bool ClearUsesEmptyStdin)
{
    /// <summary>
    /// Returns a variant of this helper set targeting the X11/Wayland PRIMARY selection
    /// instead of CLIPBOARD. Ignored on helpers that have no primary concept (pbcopy).
    /// </summary>
    public ClipboardHelperSet WithPrimary()
    {
        switch (Name)
        {
            case "xclip":
                return this with
                {
                    CopyArgs = ReplaceArg(CopyArgs, "clipboard", "primary"),
                    PasteArgs = ReplaceArg(PasteArgs, "clipboard", "primary"),
                    ClearArgs = ReplaceArg(ClearArgs, "clipboard", "primary"),
                };

            case "xsel":
                return this with
                {
                    CopyArgs = ReplaceArg(CopyArgs, "--clipboard", "--primary"),
                    PasteArgs = ReplaceArg(PasteArgs, "--clipboard", "--primary"),
                    ClearArgs = ReplaceArg(ClearArgs, "--clipboard", "--primary"),
                };

            case "wl-clipboard":
                return this with
                {
                    CopyArgs = AppendArg(CopyArgs, "--primary"),
                    PasteArgs = AppendArg(PasteArgs, "--primary"),
                    ClearArgs = AppendArg(ClearArgs, "--primary"),
                };

            default:
                return this;
        }
    }

    private static IReadOnlyList<string> ReplaceArg(IReadOnlyList<string> args, string from, string to)
    {
        var list = new List<string>(args.Count);
        foreach (string a in args)
        {
            list.Add(a == from ? to : a);
        }
        return list;
    }

    private static IReadOnlyList<string> AppendArg(IReadOnlyList<string> args, string extra)
    {
        var list = new List<string>(args.Count + 1);
        list.AddRange(args);
        list.Add(extra);
        return list;
    }
}
